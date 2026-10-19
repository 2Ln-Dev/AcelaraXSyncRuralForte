using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AceleraXSync.Acelera;
using AceleraXSync.Core;
using AceleraXSync.Dll;
using TwoLN.Licensing;

namespace AceleraXSync.Pedidos;

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
    private readonly OrigemPedidoErp _origem;
    private readonly UsuarioPedidoErp _usuario;
    private readonly AgendaService _agenda;
    private readonly PedidoOptions _opt;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly LicenseState _license;
    private readonly ILogger<PedidoWorker> _log;
    private bool _licencaBloqueadaAntes;   // p/ emitir Event Log so' na transicao de estado

    public PedidoWorker(
        AceleraClient acelera,
        DllBridge dll,
        OrigemPedidoErp origem,
        UsuarioPedidoErp usuario,
        AgendaService agenda,
        IOptions<PedidoOptions> opt,
        IHostApplicationLifetime lifetime,
        LicenseState license,
        ILogger<PedidoWorker> log)
    {
        _acelera = acelera;
        _dll = dll;
        _origem = origem;
        _usuario = usuario;
        _agenda = agenda;
        _opt = opt.Value;
        _lifetime = lifetime;
        _license = license;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // SkipDll (teste): a DLL nem e' carregada; o worker so' loga o payload.
        if (!_opt.SkipDll)
        {
            try
            {
                _dll.Carregar();
            }
            catch (Exception e)
            {
                // Servico UNIFICADO: a falha da DLL NAO derruba o host (mataria a carga de
                // base). Loga critico e DESLIGA so' o worker de PEDIDOS; a carga de base segue.
                // O operador corrige a DLL/AGB.ini e reinicia o servico.
                EventoServico(LogLevel.Critical, e, "Nao foi possivel carregar a DLL AGB - worker de PEDIDOS desligado (a carga de base continua). Corrija a DLL/AGB.ini e reinicie o servico.");
                return;
            }
        }

        // Aguarda a 1a validacao de licenca concluir (o LicenseChecker do pacote roda
        // em paralelo) para nao pular a 1a rodada pelo estado inicial "Verificando...".
        await AguardarPrimeiraChecagemLicencaAsync(stoppingToken);

        // Modo one-shot: uma rodada e encerra (Task Scheduler / teste). Espelha o
        // default do run.py Python (loop era opt-in).
        if (_opt.RunOnce)
        {
            // Servico UNIFICADO: o one-shot de pedidos NAO encerra o host (co-hospeda a carga
            // de base). Faz a rodada e retorna; quem controla o fim do processo e' a carga de
            // base no modo DryRun.
            if (LicencaBloqueada()) return;
            try { await RodadaAsync(stoppingToken); }
            catch (ApiAuthException e) { _log.LogCritical(e, "401 na API AceleraX."); }
            catch (Exception e) { _log.LogError(e, "Falha na rodada one-shot."); }
            return;
        }

        _log.LogInformation("Worker de pedidos iniciado: segue a agenda 'pedidos' do AceleraX (conferida a cada {Tick}s; " +
            "sem agenda, intervalo fixo de {PollSeconds}s) desde={Desde}",
            _opt.AgendaTickSeconds, _opt.PollSeconds, _opt.Desde ?? "-");

        DateTimeOffset? ultimaFixa = null;
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
                    var agenda = await _agenda.ObterAsync(stoppingToken);
                    if (agenda is not null)
                    {
                        if (agenda.TryGetValue(EntidadeAgenda, out var item)
                            && _agenda.Devida(item, DateTimeOffset.Now, out var forcada))
                            await RodadaAgendadaAsync(item, forcada, stoppingToken);
                    }
                    else if (ultimaFixa is null
                             || DateTimeOffset.Now - ultimaFixa.Value >= TimeSpan.FromSeconds(_opt.PollSeconds))
                    {
                        // Sem agenda (app ainda sem a tela/rota): intervalo fixo do appsettings.
                        ultimaFixa = DateTimeOffset.Now;
                        await RodadaAsync(stoppingToken);
                    }
                }
                catch (ApiAuthException e)
                {
                    // Servico UNIFICADO: 401 do lado pedido desliga so' este worker (a carga
                    // de base tem a propria chave/auth e segue). Nao derruba o host.
                    EventoServico(LogLevel.Critical, e, "401 na API AceleraX (pedidos) - worker de PEDIDOS desligado (a carga de base continua). Confira a X-Api-Key.");
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // parada normal do servico
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Falha na rodada â€” sera' retentado na proxima.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _opt.AgendaTickSeconds)), stoppingToken); }
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
                EventoServico(LogLevel.Information, null, "Licenca valida novamente â€” sincronizacao RETOMADA.");
            }
            return false;
        }
        if (!_licencaBloqueadaAntes)
        {
            _licencaBloqueadaAntes = true;   // transicao licenciado->bloqueado: vai ao Event Log
            EventoServico(LogLevel.Warning, null,
                "Licenca NAO valida â€” sincronizacao PAUSADA. Motivo: {Motivo}. Codigo da maquina (fingerprint, informe ao fornecedor p/ emitir a licenca): {Fingerprint}. O servico continua de pe e retoma quando a licenca voltar.",
                s.Motivo, s.Fingerprint);
        }
        else
        {
            // segue bloqueado: so' no arquivo (evita floodar o Event Log a cada rodada)
            _log.LogWarning("Licenca ainda invalida ({Motivo}) â€” sincronizacao segue pausada.", s.Motivo);
        }
        return true;
    }

    /// <summary>Emite um EVENTO DE SERVICO (vai tambem para o Windows Event Log via a
    /// propriedade EvtServico). So' para eventos importantes do servico â€” NAO para eventos
    /// normais de sincronizacao (esses ficam so' no arquivo/console).</summary>
    private void EventoServico(LogLevel nivel, Exception? ex, string template, params object?[] args)
    {
        using (_log.BeginScope(new Dictionary<string, object> { ["EvtServico"] = true }))
            _log.Log(nivel, ex, template, args);
    }

    /// <summary>Nome do item de pedidos na agenda do AceleraX.</summary>
    private const string EntidadeAgenda = "pedidos";

    /// <summary>Rodada disparada pela agenda (na hora ou forcada): roda e devolve o
    /// resultado para a tela "Agenda de sincronizacao".</summary>
    private async Task RodadaAgendadaAsync(AgendaItem item, bool forcada, CancellationToken ct)
    {
        var inicio = DateTimeOffset.Now;
        Execucao ex;
        try
        {
            var r = await RodadaAsync(ct);
            var msg = $"{r.Total} pedido(s): {r.Integrados} integrado(s), {r.ErroPermanente} com erro, {r.Transitorio} pendente(s).";
            ex = new Execucao(inicio, DateTimeOffset.Now, r.ErroPermanente > 0 ? "ERRO" : "OK", msg, r.Integrados, forcada);
        }
        catch (ApiAuthException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            _log.LogError(e, "Falha na rodada de pedidos - sera' retentado.");
            ex = new Execucao(inicio, DateTimeOffset.Now, "ERRO", e.Message, null, forcada);
        }
        await _agenda.RegistrarAsync(EntidadeAgenda, item, ex, ct);
    }

    private sealed record ResumoRodada(int Total, int Integrados, int ErroPermanente, int Transitorio);

    private async Task<ResumoRodada> RodadaAsync(CancellationToken ct)
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
                await LogTransitorioAsync(p, "OUTRO", e.GetType().Name + ": " + e.Message, null, ct);
                _log.LogWarning(e, "pedido={CodiPed} status=TRANSITORIO â€” segue pendente (retry)", p.CodiPed);
            }
        }
        _log.LogInformation("Rodada: total={Total} integrados={Integrados} erro_permanente={ErroPerm} transitorio={Transit}",
            itens.Count, integrados, errosPerm, transit);
        return new ResumoRodada(itens.Count, integrados, errosPerm, transit);
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

        // A tabela de preco e' do AceleraX: o payloadErp ja' vem com o codigoTabelaPreco
        // de cada item. O sync nao gera nem altera tabela no ERP — item sem tabela e'
        // erro do pedido (PERMANENTE), nem chega na DLL.
        var semTabela = (payload["pedidoVendaItensList"]?.AsArray() ?? new())
            .Where(i => SemTabela(i?["codigoTabelaPreco"]))
            .Select(i => i?["codigoProduto"]?.ToString() ?? "?")
            .ToList();
        if (semTabela.Count > 0)
            return await MarcarPermanenteAsync(p, "TABELA_INVALIDA",
                "Item(ns) sem codigoTabelaPreco (tabela de preco do ERP): " + string.Join(", ", semTabela), ct);

        if (_opt.SkipDll)
        {
            _log.LogWarning("SkipDll: DLL/integrar PULADOS. {CodiPed} payload={Payload}",
                p.CodiPed, payload.ToJsonString());
            return Desfecho.Transitorio;
        }

        // 2) DLL, com o usuario do vendedor do pedido (o AceleraX manda 'ADM' fixo).
        await _usuario.AplicarAsync(p.CodiPed, payload, ct);
        var resp = _dll.Incluir(payload);
        var tentativa = new Tentativa(payload, resp.Bruto);

        if (resp.Sucesso)
        {
            // A DLL marca o pedido como gerado pela SiagriApi (ORIG_PED = 'S'): tira a marca.
            await _origem.LimparAsync(p.CodiPed, payload, resp.NumeroPedido, ct);

            // GUARDA de idempotencia: a partir daqui o pedido JA ESTA no ERP. Se o /integrar
            // falhar, e' TRANSITORIO (nunca ERR) â€” a proxima rodada da RNRPO002 e confirma.
            try
            {
                await _acelera.MarcarIntegradoAsync(p.CodiPed, resp.NumeroPedido, p.Origem, ct, tentativa);
            }
            catch (Exception e)
            {
                await LogTransitorioAsync(p, "INTEGRAR_FALHOU",
                    $"Gravado no ERP (numero {resp.NumeroPedido}) mas o /integrar falhou: {e.Message}", tentativa, ct);
                _log.LogWarning(e, "pedido={CodiPed} GRAVADO no ERP (nÂº {Numero}) mas /integrar falhou â€” segue pendente, reconfirma por RNRPO002",
                    p.CodiPed, resp.NumeroPedido);
                return Desfecho.Transitorio;
            }
            _log.LogInformation("Gravado: {CodiPed} numero_erp={Numero} status={Status}", p.CodiPed, resp.NumeroPedido, resp.Status);
            return Desfecho.Integrado;
        }

        if (resp.JaCadastrado)
        {
            // RNRPO002: pedido ja existe no ERP â€” idempotencia preservada (inalterado).
            await _acelera.MarcarIntegradoAsync(p.CodiPed, resp.NumeroPedido, p.Origem, ct, tentativa);
            _log.LogInformation("Ja integrado (RNRPO002): {CodiPed} â€” {Msg}", p.CodiPed, Trunc(resp.Mensagem, 200));
            return Desfecho.Integrado;
        }

        // CON_xxx = a DLL nao conectou na base (driver/cliente Oracle/AGB.ini). Problema de
        // AMBIENTE, nao do pedido: TRANSITORIO (fica pendente; retenta na proxima rodada).
        if ((resp.Mensagem ?? "").Contains("CON_0"))
        {
            _log.LogError("pedido={CodiPed} DLL SEM CONEXAO COM O ERP - segue pendente: {Msg}", p.CodiPed, Trunc(resp.Mensagem, 400));
            await LogTransitorioAsync(p, "SEM_CONEXAO_ERP", resp.Mensagem, tentativa, ct);
            return Desfecho.Transitorio;
        }

        // Rejeicao de NEGOCIO da DLL = PERMANENTE (a DLL recusou; NADA foi gravado no ERP).
        var tipoDll = (resp.Mensagem ?? "").Contains("RCTA013") ? "PRODUTO_FORA_TABELA" : "REJEICAO_DLL";
        return await MarcarPermanenteAsync(p, tipoDll, resp.Mensagem ?? "", ct, tentativa);
    }

    /// <summary>Registra ERRO PERMANENTE no AceleraX (POST /erro) â€” o pedido sai da fila.
    /// Best-effort: se o /erro falhar (ex.: endpoint ainda nao deployado), NAO quebra â€”
    /// degrada para o comportamento atual (segue pendente). 401 propaga.</summary>
    private async Task<Desfecho> MarcarPermanenteAsync(PedidoResumo p, string tipoErro, string mensagem, CancellationToken ct,
        Tentativa? tentativa = null)
    {
        // Mensagem COMPLETA: o servidor corta em 400 so' no estado; o historico guarda tudo.
        var msg = Trunc(mensagem, 8000);
        _log.LogError("pedido={CodiPed} status=ERRO tipo_erro={TipoErro} mensagem={Mensagem}", p.CodiPed, tipoErro, msg);
        try
        {
            await _acelera.MarcarErroAsync(p.CodiPed, tipoErro, msg, ct, tentativa);
            return Desfecho.ErroPermanente;
        }
        catch (ApiAuthException)
        {
            throw; // 401 interrompe o lote
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "pedido={CodiPed} â€” falha ao marcar ERRO no AceleraX; segue pendente", p.CodiPed);
            return Desfecho.Transitorio;
        }
    }

    /// <summary>Registra no historico do AceleraX (POST /log) uma tentativa TRANSITORIA.
    /// Best-effort: falhar aqui nunca muda o desfecho do pedido (so' loga no arquivo).</summary>
    private async Task LogTransitorioAsync(PedidoResumo p, string tipo, string? mensagem, Tentativa? tentativa, CancellationToken ct)
    {
        try { await _acelera.RegistrarLogAsync(p.CodiPed, tipo, Trunc(mensagem, 8000), ct, tentativa); }
        catch (ApiAuthException) { throw; }
        catch (Exception e) { _log.LogWarning("pedido={CodiPed} - falha ao registrar o log transitorio no AceleraX: {Msg}", p.CodiPed, e.Message); }
    }

    private static bool SemTabela(JsonNode? n)
    {
        var s = n?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) || s == "0";
    }

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n]);
}
