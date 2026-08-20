using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaseSync.Service.Acelera;
using BaseSync.Service.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwoLN.Licensing;

namespace BaseSync.Service.Sync;

/// <summary>
/// Worker da CARGA DE BASE (ERP -> AceleraX). Port de sync.py para BackgroundService,
/// no mesmo molde do PedidoErp.Service.
///
/// Fluxo por entidade (full sync, igual ao Python):
///   SELECT completo da view -> transformacao -> chunks -> POST -> UPSERT no servidor.
///
/// Contrato de erro (igual ao sync.py):
///  - OracleReadException  -> loga, PULA a entidade, segue para a proxima (exit 1)
///  - ApiAuthException 401 -> loga e INTERROMPE a rodada inteira
///  - erro de chunk        -> loga cada um, marca exit 1 e segue
/// A tabela de preco sai do loop das entidades simples: header + itens vao juntos no
/// endpoint combinado, no fim.
/// </summary>
public sealed class BaseSyncWorker : BackgroundService
{
    private readonly OracleReader _oracle;
    private readonly BaseApiClient _api;
    private readonly SyncOptions _opt;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly LicenseState _license;
    private readonly ILogger<BaseSyncWorker> _log;
    private bool _licencaBloqueadaAntes;

    public BaseSyncWorker(
        OracleReader oracle,
        BaseApiClient api,
        IOptions<SyncOptions> opt,
        IHostApplicationLifetime lifetime,
        LicenseState license,
        ILogger<BaseSyncWorker> log)
    {
        _oracle = oracle;
        _api = api;
        _opt = opt.Value;
        _lifetime = lifetime;
        _license = license;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await AguardarPrimeiraChecagemLicencaAsync(stoppingToken);

        if (_opt.RunOnce)
        {
            if (LicencaBloqueada()) { _lifetime.StopApplication(); return; }
            try { await RodadaAsync(stoppingToken); }
            catch (ApiAuthException e) { _log.LogCritical(e, "401 na API AceleraX."); }
            catch (Exception e) { _log.LogError(e, "Falha na rodada one-shot."); }
            _lifetime.StopApplication();
            return;
        }

        _log.LogInformation("Sync de base iniciado. Intervalo={IntervalSeconds}s batch={BatchSize}",
            _opt.IntervalSeconds, _opt.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
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
                    break;
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Falha na rodada — sera' retentado na proxima.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_opt.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Uma rodada completa: todas as entidades do SYNC_ORDER, na ordem.</summary>
    private async Task RodadaAsync(CancellationToken ct)
    {
        var cronoTotal = Stopwatch.StartNew();
        var falhou = false;

        // 1. Entidades simples: 1 entidade = 1 endpoint. codi_ten NAO e' enviado (vem da
        //    API key); o servidor faz o upsert idempotente.
        foreach (var entidade in TablesSpec.SyncOrder)
        {
            ct.ThrowIfCancellationRequested();
            if (entidade is TablesSpec.PrecoHeader or TablesSpec.PrecoItens) continue;
            if (!string.IsNullOrWhiteSpace(_opt.Somente) && entidade != _opt.Somente) continue;

            if (!await ProcessarEntidadeAsync(entidade, ct)) falhou = true;
        }

        // 2. Tabela de preco (header + itens no mesmo request). O servidor resolve
        //    erp_id -> codi_tab e ignora itens orfaos (reportados aqui).
        var rodaPreco = string.IsNullOrWhiteSpace(_opt.Somente)
                        || _opt.Somente is TablesSpec.PrecoHeader or TablesSpec.PrecoItens;
        if (rodaPreco && !await ProcessarTabelaPrecoAsync(ct)) falhou = true;

        cronoTotal.Stop();
        _log.LogInformation("Rodada concluida em {Segundos}s — resultado={Resultado}",
            Math.Round(cronoTotal.Elapsed.TotalSeconds, 1), falhou ? "COM ERROS" : "OK");
    }

    /// <returns>false se a entidade teve erro (equivale ao exit_code=1 do Python).</returns>
    private async Task<bool> ProcessarEntidadeAsync(string entidade, CancellationToken ct)
    {
        var spec = TablesSpec.Entidades[entidade];
        var crono = Stopwatch.StartNew();
        _log.LogInformation("entidade={Entidade} status=inicio view={View}", entidade, spec.OracleView);

        List<Dictionary<string, object?>> linhas;
        try
        {
            linhas = await _oracle.FetchAsync(entidade, ct);
        }
        catch (OracleReadException e)
        {
            _log.LogError(e, "entidade={Entidade} erro=oracle_connection — entidade PULADA", entidade);
            return false;
        }

        var itens = Transformer.Transform(linhas, entidade);
        var origem = itens.Count;
        if (_opt.Limite is > 0) itens = Limitar(itens, _opt.Limite.Value);

        if (_opt.DryRun)
        {
            var arquivo = Dump(entidade, itens);
            crono.Stop();
            _log.LogWarning("entidade={Entidade} DRY-RUN origem={Origem} transformados={Enviados} arquivo={Arquivo} tempo={Segundos}s",
                entidade, origem, itens.Count, arquivo, Math.Round(crono.Elapsed.TotalSeconds, 1));
            return true;
        }

        var resultado = await _api.PostItemsAsync(spec.Slug!, itens,
            (inicio, rec, grv) => _log.LogInformation(
                "entidade={Entidade} chunk={Inicio} enviados={Enviados} gravados={Gravados}", entidade, inicio, rec, grv),
            ct);

        foreach (var erro in resultado.Erros)
            _log.LogError("entidade={Entidade} erro=api_post chunk={Inicio} detalhe={Detalhe}",
                entidade, erro.BatchStart, erro.Erro);

        crono.Stop();
        _log.LogInformation(
            "entidade={Entidade} status={Status} origem={Origem} enviados={Enviados} gravados={Gravados} rejeitados={Rejeitados} erros={Erros} tempo={Segundos}s",
            entidade, resultado.Erros.Count == 0 ? "sucesso" : "com_erros",
            origem, resultado.Enviados, resultado.Gravados,
            Math.Max(0, resultado.Enviados - resultado.Gravados), resultado.Erros.Count,
            Math.Round(crono.Elapsed.TotalSeconds, 1));

        return resultado.Erros.Count == 0;
    }

    private async Task<bool> ProcessarTabelaPrecoAsync(CancellationToken ct)
    {
        var crono = Stopwatch.StartNew();
        _log.LogInformation("entidade={Entidade} status=inicio (header + itens no endpoint combinado)",
            TablesSpec.PrecoHeader);

        JsonArray headers, itens;
        try
        {
            headers = Transformer.Transform(await _oracle.FetchAsync(TablesSpec.PrecoHeader, ct), TablesSpec.PrecoHeader);
            itens = Transformer.Transform(await _oracle.FetchAsync(TablesSpec.PrecoItens, ct), TablesSpec.PrecoItens);
        }
        catch (OracleReadException e)
        {
            _log.LogError(e, "entidade={Entidade} erro=oracle_connection", TablesSpec.PrecoHeader);
            return false;
        }

        var origemHeaders = headers.Count;
        var origemItens = itens.Count;
        if (_opt.Limite is > 0)
        {
            headers = Limitar(headers, _opt.Limite.Value);
            itens = Limitar(itens, _opt.Limite.Value);
        }

        if (_opt.DryRun)
        {
            var a1 = Dump(TablesSpec.PrecoHeader, headers);
            var a2 = Dump(TablesSpec.PrecoItens, itens);
            crono.Stop();
            _log.LogWarning("entidade=tabela_preco DRY-RUN headers={Headers} itens={Itens} arquivos={A1},{A2} tempo={Segundos}s",
                headers.Count, itens.Count, a1, a2, Math.Round(crono.Elapsed.TotalSeconds, 1));
            return true;
        }

        var r = await _api.PostTabelasPrecoAsync(headers, itens,
            (inicio, rec, grv) => _log.LogInformation(
                "entidade=itens_tabela_preco chunk={Inicio} enviados={Enviados} gravados={Gravados}", inicio, rec, grv),
            ct);

        foreach (var erro in r.Erros)
            _log.LogError("entidade={Entidade} erro=api_post chunk={Inicio} detalhe={Detalhe}",
                TablesSpec.PrecoHeader, erro.BatchStart, erro.Erro);

        if (r.Orfaos > 0)
            _log.LogError("entidade={Entidade} erro=itens_orfaos detalhe={Orfaos} itens sem tabela de preco correspondente",
                TablesSpec.PrecoItens, r.Orfaos);

        crono.Stop();
        _log.LogInformation(
            "entidade=tabela_preco status={Status} origem_headers={OrigemH} origem_itens={OrigemI} headers_gravados={HG} itens_gravados={IG} orfaos={Orfaos} erros={Erros} tempo={Segundos}s",
            r.Erros.Count == 0 && r.Orfaos == 0 ? "sucesso" : "com_erros",
            origemHeaders, origemItens, r.HeadersGravados, r.ItensGravados, r.Orfaos, r.Erros.Count,
            Math.Round(crono.Elapsed.TotalSeconds, 1));

        return r.Erros.Count == 0 && r.Orfaos == 0;
    }

    // --- Auxiliares ---------------------------------------------------------

    private static JsonArray Limitar(JsonArray origem, int limite)
    {
        var saida = new JsonArray();
        for (var i = 0; i < Math.Min(limite, origem.Count); i++)
            saida.Add(origem[i]?.DeepClone());
        return saida;
    }

    /// <summary>Grava o payload transformado (DryRun) para a comparacao Python x .NET.</summary>
    private string Dump(string entidade, JsonArray itens)
    {
        var dir = Path.IsPathRooted(_opt.DumpDir)
            ? _opt.DumpDir
            : Path.Combine(AppContext.BaseDirectory, _opt.DumpDir);
        Directory.CreateDirectory(dir);
        var arquivo = Path.Combine(dir, $"{entidade}.json");
        File.WriteAllText(arquivo, itens.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return arquivo;
    }

    // --- Licenca (mesmo gate do PedidoErp.Service) --------------------------

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
            _licencaBloqueadaAntes = true;
            EventoServico(LogLevel.Warning, null,
                "Licenca NAO valida — sincronizacao PAUSADA. Motivo: {Motivo}. Codigo da maquina (fingerprint, informe ao fornecedor p/ emitir a licenca): {Fingerprint}. O servico continua de pe e retoma quando a licenca voltar.",
                s.Motivo, s.Fingerprint);
        }
        else
        {
            _log.LogWarning("Licenca ainda invalida ({Motivo}) — sincronizacao segue pausada.", s.Motivo);
        }
        return true;
    }

    private void EventoServico(LogLevel nivel, Exception? ex, string template, params object?[] args)
    {
        using (_log.BeginScope(new Dictionary<string, object> { ["EvtServico"] = true }))
            _log.Log(nivel, ex, template, args);
    }
}
