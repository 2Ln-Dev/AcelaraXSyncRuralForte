using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoErp.Service.Acelera;
using PedidoErp.Service.Core;
using PedidoErp.Service.Dll;
using PedidoErp.Service.Tabela;
using TwoLN.Licensing;

namespace PedidoErp.Service.Pedidos;

/// <summary>
/// Worker do fluxo inverso: puxa pedidos aprovados do AceleraX, grava cada um pela
/// DLL e marca o par (pedido) como integrado. Port de pedido_erp/service.py +
/// run.py (modo loop) para BackgroundService.
///
/// Contrato de erro (igual ao Python):
/// - ApiAuthException (401)   -> interrompe o servico (chave invalida).
/// - Erro de negocio da DLL   -> loga, NAO marca integrado, segue para o proximo.
/// - Erro de rede/servidor    -> loga, segue; retentado na proxima rodada.
/// </summary>
public sealed class PedidoWorker : BackgroundService
{
    private readonly AceleraClient _acelera;
    private readonly DllBridge _dll;
    private readonly ITabelaBarterService _tabelas;
    private readonly PedidoOptions _opt;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly LicenseState _license;
    private readonly ILogger<PedidoWorker> _log;
    private bool _licencaBloqueadaAntes;   // p/ emitir Event Log so' na transicao de estado

    public PedidoWorker(
        AceleraClient acelera,
        DllBridge dll,
        ITabelaBarterService tabelas,
        IOptions<PedidoOptions> opt,
        IHostApplicationLifetime lifetime,
        LicenseState license,
        ILogger<PedidoWorker> log)
    {
        _acelera = acelera;
        _dll = dll;
        _tabelas = tabelas;
        _opt = opt.Value;
        _lifetime = lifetime;
        _license = license;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _dll.Carregar();
        }
        catch (Exception e)
        {
            EventoServico(LogLevel.Critical, e, "Nao foi possivel carregar a DLL — encerrando o servico.");
            _lifetime.StopApplication();
            return;
        }

        // Aguarda a 1a validacao de licenca concluir (o LicenseChecker do pacote roda
        // em paralelo) para nao pular a 1a rodada pelo estado inicial "Verificando...".
        await AguardarPrimeiraChecagemLicencaAsync(stoppingToken);

        // Modo one-shot: uma rodada e encerra (Task Scheduler / teste). Espelha o
        // default do run.py Python (loop era opt-in).
        if (_opt.RunOnce)
        {
            if (LicencaBloqueada()) { _lifetime.StopApplication(); return; }
            try { await RodadaAsync(stoppingToken); }
            catch (ApiAuthException e) { _log.LogCritical(e, "401 na API AceleraX."); }
            catch (Exception e) { _log.LogError(e, "Falha na rodada one-shot."); }
            _lifetime.StopApplication();
            return;
        }

        _log.LogInformation("Worker iniciado. Intervalo={PollSeconds}s desde={Desde}",
            _opt.PollSeconds, _opt.Desde ?? "-");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Gate de licenca: invalida/expirada/suspensa/maquina-ou-produto errados =>
            // NAO sincroniza, mas o servico segue de pe e RETOMA sozinho quando a licenca
            // voltar a valer (o LicenseChecker revalida em background). Servidor de licenca
            // fora do ar dentro do grace period conta como valido (o pacote resolve isso).
            if (!LicencaBloqueada())
            {
                try
                {
                    await RodadaAsync(stoppingToken);
                }
                catch (ApiAuthException e)
                {
                    _log.LogCritical(e, "401 na API AceleraX — encerrando o servico.");
                    _lifetime.StopApplication();
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // parada normal do servico
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Falha na rodada — sera' retentado na proxima.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // --- Licenca (gate; a validacao real e' do pacote 2LN.Licensing via LicenseState) ---

    /// <summary>Da' ate ~10s para a 1a checagem de licenca concluir (o LicenseChecker roda
    /// em paralelo), evitando pular a 1a rodada pelo estado inicial "Verificando...".</summary>
    private async Task AguardarPrimeiraChecagemLicencaAsync(CancellationToken ct)
    {
        for (var i = 0; i < 20 && !ct.IsCancellationRequested; i++)
        {
            var s = _license.Current;
            if (s.Licenciado || !s.Motivo.StartsWith("Verificando", StringComparison.OrdinalIgnoreCase))
                return;
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>true = licenca NAO valida -> pula a sincronizacao (servico segue de pe).
    /// A validacao (offline assinada + online suspend-only + grace period) e' toda do
    /// pacote 2LN.Licensing; aqui so' consultamos o resultado em LicenseState.Current.</summary>
    private bool LicencaBloqueada()
    {
        var s = _license.Current;
        if (s.Licenciado)
        {
            if (_licencaBloqueadaAntes)
            {
                _licencaBloqueadaAntes = false;
                EventoServico(LogLevel.Information, null, "Licenca valida novamente — sincronizacao RETOMADA.");
            }
            return false;
        }
        if (!_licencaBloqueadaAntes)
        {
            _licencaBloqueadaAntes = true;   // transicao licenciado->bloqueado: vai ao Event Log
            EventoServico(LogLevel.Warning, null,
                "Licenca NAO valida — sincronizacao PAUSADA. Motivo: {Motivo}. Codigo da maquina (fingerprint, informe ao fornecedor p/ emitir a licenca): {Fingerprint}. O servico continua de pe e retoma quando a licenca voltar.",
                s.Motivo, s.Fingerprint);
        }
        else
        {
            // segue bloqueado: so' no arquivo (evita floodar o Event Log a cada rodada)
            _log.LogWarning("Licenca ainda invalida ({Motivo}) — sincronizacao segue pausada.", s.Motivo);
        }
        return true;
    }

    /// <summary>Emite um EVENTO DE SERVICO (vai tambem para o Windows Event Log via a
    /// propriedade EvtServico). So' para eventos importantes do servico — NAO para eventos
    /// normais de sincronizacao (esses ficam so' no arquivo/console).</summary>
    private void EventoServico(LogLevel nivel, Exception? ex, string template, params object?[] args)
    {
        using (_log.BeginScope(new Dictionary<string, object> { ["EvtServico"] = true }))
            _log.Log(nivel, ex, template, args);
    }

    private async Task RodadaAsync(CancellationToken ct)
    {
        var pendentes = await _acelera.ListarIntegraveisAsync(_opt.Desde, ct);
        IEnumerable<PedidoResumo> filtrados = pendentes;
        if (!string.IsNullOrWhiteSpace(_opt.Somente))
            filtrados = filtrados.Where(p => p.CodiPed == _opt.Somente);
        if (_opt.Limite is > 0)
            filtrados = filtrados.Take(_opt.Limite.Value);
        var itens = filtrados.ToList();
        _log.LogInformation("Listar: {Total} pendente(s) desde={Desde}", itens.Count, _opt.Desde ?? "-");

        int integrados = 0, errosPerm = 0, transit = 0;
        foreach (var p in itens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (await ProcessarUmAsync(p, ct))
                {
                    case Desfecho.Integrado: integrados++; break;
                    case Desfecho.ErroPermanente: errosPerm++; break;
                    default: transit++; break;   // Transitorio
                }
            }
            catch (ApiAuthException)
            {
                throw; // 401 interrompe o lote inteiro
            }
            catch (Exception e)
            {
                // Transitorio imprevisto (rede/Oracle/DLL): deixa PENDENTE, retenta na proxima rodada.
                transit++;
                _log.LogWarning(e, "pedido={CodiPed} status=TRANSITORIO — segue pendente (retry)", p.CodiPed);
            }
        }
        _log.LogInformation("Rodada: total={Total} integrados={Integrados} erro_permanente={ErroPerm} transitorio={Transit}",
            itens.Count, integrados, errosPerm, transit);
    }

    // Desfecho por pedido: Integrado (sai da fila OK), ErroPermanente (sai da fila com ERR),
    // Transitorio (fica pendente para retry). Erros permanentes JA foram registrados no
    // AceleraX (POST /erro) aqui dentro; transitorios propagam ou retornam Transitorio.
    private enum Desfecho { Integrado, ErroPermanente, Transitorio }

    private async Task<Desfecho> ProcessarUmAsync(PedidoResumo p, CancellationToken ct)
    {
        // 1) Payload. 4xx (!=401) = problema no pedido => PERMANENTE; transitorio/401 propagam.
        JsonObject? payload;
        try
        {
            payload = await _acelera.ObterPayloadAsync(p.CodiPed, p.Origem, ct);
        }
        catch (ApiException e) when (!e.Transitorio)
        {
            return await MarcarPermanenteAsync(p, "DADOS_INVALIDOS", e.Message, ct);
        }

        if (payload is null)
            return await MarcarPermanenteAsync(p, "PAYLOAD_INVALIDO", "resposta sem payloadErp", ct);

        // 2) Materializa a tabela barter. ErroNegocioTabela = PERMANENTE; OracleException/
        //    timeout NAO sao capturados aqui -> propagam como TRANSITORIO (fica pendente).
        try
        {
            await _tabelas.PreencherAsync(payload, ct);
        }
        catch (ErroNegocioTabela e)
        {
            var tipo = e.Tipo
                ?? (e.Message.Contains("sem tabela p/ origem") ? "PRODUTO_FORA_TABELA" : "TABELA_INVALIDA");
            return await MarcarPermanenteAsync(p, tipo, e.Message, ct);
        }

        if (_opt.SkipDll)
        {
            var codsTab = string.Join(",", (payload["pedidoVendaItensList"]?.AsArray() ?? new())
                .Select(i => i?["codigoTabelaPreco"]?.ToString()));
            _log.LogWarning("SkipDll: tabela materializada, DLL/integrar PULADOS. {CodiPed} codigoTabelaPreco=[{Cods}] dataCotacao={Data}",
                p.CodiPed, codsTab, payload["dataCotacao"]?.ToString() ?? "-");
            return Desfecho.Integrado;
        }

        // 3) DLL
        var resp = _dll.Incluir(payload);

        if (resp.Sucesso)
        {
            // GUARDA de idempotencia: a partir daqui o pedido JA ESTA no ERP. Se o /integrar
            // falhar, e' TRANSITORIO (nunca ERR) — a proxima rodada da RNRPO002 e confirma.
            try
            {
                await _acelera.MarcarIntegradoAsync(p.CodiPed, resp.NumeroPedido, p.Origem, ct);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "pedido={CodiPed} GRAVADO no ERP (nº {Numero}) mas /integrar falhou — segue pendente, reconfirma por RNRPO002",
                    p.CodiPed, resp.NumeroPedido);
                return Desfecho.Transitorio;
            }
            _log.LogInformation("Gravado: {CodiPed} numero_erp={Numero} status={Status}", p.CodiPed, resp.NumeroPedido, resp.Status);
            return Desfecho.Integrado;
        }

        if (resp.JaCadastrado)
        {
            // RNRPO002: pedido ja existe no ERP — idempotencia preservada (inalterado).
            await _acelera.MarcarIntegradoAsync(p.CodiPed, resp.NumeroPedido, p.Origem, ct);
            _log.LogInformation("Ja integrado (RNRPO002): {CodiPed} — {Msg}", p.CodiPed, Trunc(resp.Mensagem, 200));
            return Desfecho.Integrado;
        }

        // Rejeicao de NEGOCIO da DLL = PERMANENTE (a DLL recusou; NADA foi gravado no ERP).
        var tipoDll = (resp.Mensagem ?? "").Contains("RCTA013") ? "PRODUTO_FORA_TABELA" : "REJEICAO_DLL";
        return await MarcarPermanenteAsync(p, tipoDll, resp.Mensagem ?? "", ct);
    }

    /// <summary>Registra ERRO PERMANENTE no AceleraX (POST /erro) — o pedido sai da fila.
    /// Best-effort: se o /erro falhar (ex.: endpoint ainda nao deployado), NAO quebra —
    /// degrada para o comportamento atual (segue pendente). 401 propaga.</summary>
    private async Task<Desfecho> MarcarPermanenteAsync(PedidoResumo p, string tipoErro, string mensagem, CancellationToken ct)
    {
        var msg = Trunc(mensagem, 400);
        _log.LogError("pedido={CodiPed} status=ERRO tipo_erro={TipoErro} mensagem={Mensagem}", p.CodiPed, tipoErro, msg);
        try
        {
            await _acelera.MarcarErroAsync(p.CodiPed, tipoErro, msg, ct);
            return Desfecho.ErroPermanente;
        }
        catch (ApiAuthException)
        {
            throw; // 401 interrompe o lote
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "pedido={CodiPed} — falha ao marcar ERRO no AceleraX; segue pendente", p.CodiPed);
            return Desfecho.Transitorio;
        }
    }

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n]);
}
