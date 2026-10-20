using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using AceleraXSync.Acelera;
using AceleraXSync.Core;
using AceleraXSync.Core.Data;
using TwoLN.Licensing;

namespace AceleraXSync.Esteira;

/// <summary>
/// Laco da esteira de faturamento (ver <see cref="EsteiraSync"/>). Segue a agenda 'esteira' do AceleraX, no molde do
/// <see cref="Pedidos.SituacaoPedidoWorker"/>; sem agenda, roda no intervalo fixo de <see cref="EsteiraOptions"/>.
/// Sem as tabelas do HUB (ORA-00942: HUB sem a esteira) so' avisa uma vez e segue tentando.
/// </summary>
public sealed class EsteiraWorker : BackgroundService
{
    private const string EntidadeAgenda = "esteira";

    private readonly EsteiraSync _sync;
    private readonly AgendaService _agenda;
    private readonly EsteiraOptions _opt;
    private readonly PedidoOptions _pedido;
    private readonly LicenseState _license;
    private readonly ILogger<EsteiraWorker> _log;
    private bool _avisouSemTabelas;

    public EsteiraWorker(AceleraClient acelera, IDbConnectionFactory db, AgendaService agenda, IOptions<EsteiraOptions> opt,
        IOptions<PedidoOptions> pedido, LicenseState license, ILogger<EsteiraWorker> log)
    {
        _opt = opt.Value;
        _sync = new EsteiraSync(acelera, db, log, _opt.MaxTentativas);
        _agenda = agenda;
        _pedido = pedido.Value;
        _license = license;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Ativo)
        {
            _log.LogInformation("Esteira: worker desligado (Esteira:Ativo = false).");
            return;
        }
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var intervaloFixo = TimeSpan.FromSeconds(Math.Max(30, _opt.IntervaloFixoSegundos));
        DateTimeOffset? ultimaFixa = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_license.Current.Licenciado)
            {
                try
                {
                    var agenda = await _agenda.ObterAsync(stoppingToken);
                    if (agenda is not null && agenda.TryGetValue(EntidadeAgenda, out var item))
                    {
                        if (_agenda.Devida(item, DateTimeOffset.Now, out var forcada))
                            await RodadaAgendadaAsync(item, forcada, stoppingToken);
                    }
                    else if (ultimaFixa is null || DateTimeOffset.Now - ultimaFixa.Value >= intervaloFixo)
                    {
                        // Agenda ilegivel ou sem a entidade "esteira": intervalo fixo do appsettings.
                        ultimaFixa = DateTimeOffset.Now;
                        await RodadaAsync(stoppingToken);
                    }
                }
                catch (ApiAuthException e)
                {
                    _log.LogCritical(e, "401 na API AceleraX (esteira) - worker desligado. Confira a X-Api-Key.");
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    _log.LogError(e, "Esteira: falha na rodada - sera' retentado.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _pedido.AgendaTickSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodadaAgendadaAsync(AgendaItem item, bool forcada, CancellationToken ct)
    {
        var inicio = DateTimeOffset.Now;
        Execucao ex;
        try
        {
            var r = await RodadaAsync(ct);
            ex = new Execucao(inicio, DateTimeOffset.Now, "OK", r?.ToString() ?? "Tabelas da esteira ausentes no ERP.", r?.Total, forcada);
        }
        catch (ApiAuthException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            _log.LogError(e, "Esteira: falha na rodada - sera' retentado.");
            ex = new Execucao(inicio, DateTimeOffset.Now, "ERRO", e.Message, null, forcada);
        }
        await _agenda.RegistrarAsync(EntidadeAgenda, item, ex, ct);
    }

    private async Task<RodadaEsteira?> RodadaAsync(CancellationToken ct)
    {
        try
        {
            var r = await _sync.RodadaAsync(ct);
            if (r.Total > 0 || r.FasesComErro > 0) _log.LogInformation("Esteira: {Resumo}", r);
            return r;
        }
        catch (OracleException e) when (e.Number == 942)
        {
            if (!_avisouSemTabelas)
            {
                _log.LogWarning("Esteira: tabelas APEX_EST_AX_* nao existem neste schema (o HUB 2LN ainda nao tem a esteira) - nada a fazer.");
                _avisouSemTabelas = true;
            }
            return null;
        }
    }
}
