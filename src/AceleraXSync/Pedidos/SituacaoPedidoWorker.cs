using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AceleraXSync.Acelera;
using AceleraXSync.Core;
using AceleraXSync.Core.Data;
using TwoLN.Licensing;

namespace AceleraXSync.Pedidos;

/// <summary>
/// Devolve ao AceleraX a situacao do pedido DENTRO do ERP (coluna "Situacao ERP" da
/// lista de pedidos). Segue a agenda 'pedidos_situacao' do AceleraX:
///   GET /pedidos/integrados  -> pedidos ja' integrados, com a chave no ERP
///   PEDIDO.SITU_PED          -> lido pela chave CODI_EMP + SERI_PED + PEDI_PED
///   POST /pedidos/situacao   -> so' os que mudaram
///
/// SITU_PED no dicionario do SIAGRI (RBCAMPO): 0-Nao Liberado, 1-Liberado,
/// 5-Confirmado, 9-Cancelado. Worker separado do PedidoWorker: nao precisa da DLL,
/// entao segue de pe mesmo se ela nao carregar.
/// </summary>
public sealed class SituacaoPedidoWorker : BackgroundService
{
    private const string EntidadeAgenda = "pedidos_situacao";
    // Sem agenda (app sem a tela/rota): intervalo fixo, o mesmo padrao da agenda no app.
    private static readonly TimeSpan IntervaloFixo = TimeSpan.FromMinutes(30);

    private static readonly IReadOnlyDictionary<string, string> Descricoes = new Dictionary<string, string>
    {
        ["0"] = "Não liberado",
        ["1"] = "Liberado",
        ["5"] = "Confirmado",
        ["9"] = "Cancelado",
    };

    private readonly AceleraClient _acelera;
    private readonly IDbConnectionFactory _db;
    private readonly AgendaService _agenda;
    private readonly PedidoOptions _opt;
    private readonly LicenseState _license;
    private readonly ILogger<SituacaoPedidoWorker> _log;

    public SituacaoPedidoWorker(AceleraClient acelera, IDbConnectionFactory db, AgendaService agenda,
        IOptions<PedidoOptions> opt, LicenseState license, ILogger<SituacaoPedidoWorker> log)
    {
        _acelera = acelera;
        _db = db;
        _agenda = agenda;
        _opt = opt.Value;
        _license = license;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Da' a vez ao PedidoWorker/licenca na subida (a 1a checagem de licenca leva ~segundos).
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        if (_opt.RunOnce)
        {
            if (!_license.Current.Licenciado) return;
            try { await RodadaAsync(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { _log.LogError(e, "Situacao dos pedidos: falha na rodada one-shot."); }
            return;
        }

        DateTimeOffset? ultimaFixa = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_license.Current.Licenciado)
            {
                try
                {
                    var agenda = await _agenda.ObterAsync(stoppingToken);
                    if (agenda is not null)
                    {
                        if (agenda.TryGetValue(EntidadeAgenda, out var item)
                            && _agenda.Devida(item, DateTimeOffset.Now, out var forcada))
                            await RodadaAgendadaAsync(item, forcada, stoppingToken);
                    }
                    else if (ultimaFixa is null || DateTimeOffset.Now - ultimaFixa.Value >= IntervaloFixo)
                    {
                        ultimaFixa = DateTimeOffset.Now;
                        await RodadaAsync(stoppingToken);
                    }
                }
                catch (ApiAuthException e)
                {
                    _log.LogCritical(e, "401 na API AceleraX (situacao dos pedidos) - worker desligado. Confira a X-Api-Key.");
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    _log.LogError(e, "Situacao dos pedidos: falha na rodada - sera' retentado.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _opt.AgendaTickSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodadaAgendadaAsync(AgendaItem item, bool forcada, CancellationToken ct)
    {
        var inicio = DateTimeOffset.Now;
        Execucao ex;
        try
        {
            var (conferidos, enviados) = await RodadaAsync(ct);
            ex = new Execucao(inicio, DateTimeOffset.Now, "OK",
                $"{conferidos} pedido(s) conferido(s) no ERP, {enviados} com situação nova.", enviados, forcada);
        }
        catch (ApiAuthException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            _log.LogError(e, "Situacao dos pedidos: falha na rodada - sera' retentado.");
            ex = new Execucao(inicio, DateTimeOffset.Now, "ERRO", e.Message, null, forcada);
        }
        await _agenda.RegistrarAsync(EntidadeAgenda, item, ex, ct);
    }

    /// <summary>Uma rodada: confere no ERP os integrados e manda os que mudaram.</summary>
    private async Task<(int Conferidos, int Enviados)> RodadaAsync(CancellationToken ct)
    {
        var integrados = await _acelera.ListarIntegradosAsync(_opt.SituacaoDias, ct);
        if (integrados.Count == 0) return (0, 0);

        var semEmpresa = integrados.Count(i => i.Empresa is null);
        if (semEmpresa > 0)
            _log.LogWarning("Situacao dos pedidos: {Qtd} pedido(s) sem empresa no AceleraX ficaram de fora.", semEmpresa);

        var mudaram = new List<SituacaoPedido>();
        var conferidos = 0;
        await using var conn = await _db.CreateOpenAsync(ct);
        foreach (var grupo in integrados.Where(i => i.Empresa is not null).GroupBy(i => (Emp: i.Empresa!.Value, i.Serie)))
        {
            foreach (var lote in grupo.Chunk(500))
            {
                var porNumero = lote.GroupBy(i => i.NumeroErp).ToDictionary(g => g.Key, g => g.ToList());
                var linhas = await conn.QueryAsync<(long Pedido, string? Situacao)>(new CommandDefinition(
                    "SELECT PEDI_PED, TRIM(SITU_PED) FROM PEDIDO WHERE CODI_EMP = :emp AND SERI_PED = :serie AND PEDI_PED IN :peds",
                    new { emp = grupo.Key.Emp, serie = grupo.Key.Serie, peds = porNumero.Keys.ToArray() },
                    cancellationToken: ct));

                foreach (var (pedido, situacao) in linhas)
                {
                    if (string.IsNullOrEmpty(situacao) || !porNumero.TryGetValue(pedido, out var doAcelera)) continue;
                    conferidos += doAcelera.Count;
                    foreach (var p in doAcelera.Where(p => p.Situacao != situacao))
                        mudaram.Add(new SituacaoPedido(p.CodiPed, situacao,
                            Descricoes.TryGetValue(situacao, out var d) ? d : null));
                }
            }
        }

        var enviados = mudaram.Count == 0 ? 0 : await _acelera.EnviarSituacaoAsync(mudaram, ct);
        _log.LogInformation("Situacao dos pedidos: {Integrados} integrado(s), {Conferidos} achado(s) no ERP, {Enviados} atualizado(s) no AceleraX.",
            integrados.Count, conferidos, enviados);
        return (conferidos, enviados);
    }
}
