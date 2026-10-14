using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AceleraXSync.Core;
using AceleraXSync.Sync;
using Microsoft.Extensions.Options;

namespace AceleraXSync.Acelera;

// ApiAuthException e ApiException sao definidas uma unica vez em AceleraClient.cs
// (mesmo namespace): ambos os clientes falam com a mesma API AceleraX. A versao de la'
// tem o campo extra Transitorio (default true), compativel com o uso aqui.

/// <summary>Erro de um chunk (o sync loga, marca falha e segue).</summary>
public sealed record ErroLote(int BatchStart, string Erro);

/// <summary>Resultado do upsert de uma entidade simples.</summary>
public sealed record ResultadoEntidade(int Enviados, int Gravados, IReadOnlyList<ErroLote> Erros);

/// <summary>Resultado do endpoint combinado da tabela de preco.</summary>
public sealed record ResultadoTabelaPreco(
    int HeadersGravados, int ItensGravados, int Orfaos, IReadOnlyList<ErroLote> Erros);

/// <summary>
/// Escreve os dados-base no AceleraX pela API HTTP (POST autenticado por X-Api-Key).
/// Port de api_writer.py:
///  - NAO envia codi_ten (o tenant e' resolvido no servidor pela API key);
///  - NAO resolve erp_id -> codi_tab (o endpoint de tabela de preco faz isso);
///  - um endpoint por entidade; tabela de preco (header + itens) num so' request.
/// 1 retry (5s) em rede/5xx/429; 401 -> ApiAuthException.
/// </summary>
public sealed class BaseApiClient
{
    private const int RetryDelayMs = 5000;

    private readonly HttpClient _http;
    private readonly int _batchSize;

    public BaseApiClient(HttpClient http, IOptions<AceleraOptions> opt, IOptions<SyncOptions> sync)
    {
        var o = opt.Value;
        _http = http;
        _batchSize = sync.Value.BatchSize > 0 ? sync.Value.BatchSize : 1000;

        if (string.IsNullOrWhiteSpace(o.ApiUrl)
            || !Uri.TryCreate(o.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Acelera:ApiUrl invalido ou nao configurado: '{o.ApiUrl}'. Informe a URL base do AceleraX " +
                "(ex.: https://acelera-x-app.vercel.app) no appsettings.");
        if (string.IsNullOrWhiteSpace(o.ApiKey))
            throw new InvalidOperationException(
                "Acelera:ApiKey nao configurada. Informe a X-Api-Key da integracao no appsettings.");

        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        _http.DefaultRequestHeaders.Add("X-Api-Key", o.ApiKey);
    }

    /// <summary>Upsert em lote de uma entidade simples, em chunks de BatchSize. Erros
    /// nao-auth de um chunk sao coletados (o sync marca exit 1 e segue).</summary>
    public async Task<ResultadoEntidade> PostItemsAsync(
        string slug, JsonArray linhas, Action<int, int, int>? onChunk, CancellationToken ct)
    {
        int enviados = 0, gravados = 0;
        var erros = new List<ErroLote>();

        for (var i = 0; i < linhas.Count; i += _batchSize)
        {
            var chunk = Fatiar(linhas, i, _batchSize);
            if (chunk.Count == 0) continue;
            try
            {
                var data = await PostAsync(slug, new JsonObject { ["items"] = chunk }, ct);
                var rec = (int?)data?["recebidos"] ?? chunk.Count;
                var grv = (int?)data?["gravados"] ?? 0;
                enviados += rec;
                gravados += grv;
                onChunk?.Invoke(i, rec, grv);
            }
            catch (ApiException e)
            {
                erros.Add(new ErroLote(i, e.Message));
            }
        }
        return new ResultadoEntidade(enviados, gravados, erros);
    }

    /// <summary>Header + itens da tabela de preco. O servidor resolve erp_id -> codi_tab.
    /// Os headers (poucos) sao reenviados em cada chunk de itens, pois o endpoint exige
    /// "tabelas" presente e os usa para resolver os itens. Igual ao Python.</summary>
    public async Task<ResultadoTabelaPreco> PostTabelasPrecoAsync(
        JsonArray headers, JsonArray itens, Action<int, int, int>? onChunk, CancellationToken ct)
    {
        var erros = new List<ErroLote>();
        int headersGravados = 0, itensGravados = 0, orfaos = 0;

        if (headers.Count == 0)
            return new ResultadoTabelaPreco(0, 0, 0,
                new[] { new ErroLote(0, "sem headers de tabela de preco") });

        if (itens.Count == 0)
        {
            try
            {
                var data = await PostAsync(TablesSpec.SlugTabelasPreco,
                    new JsonObject { ["tabelas"] = Clonar(headers) }, ct);
                headersGravados = (int?)data?["tabelas"]?["gravados"] ?? 0;
            }
            catch (ApiException e)
            {
                erros.Add(new ErroLote(0, e.Message));
            }
            return new ResultadoTabelaPreco(headersGravados, 0, 0, erros);
        }

        for (var i = 0; i < itens.Count; i += _batchSize)
        {
            var chunk = Fatiar(itens, i, _batchSize);
            try
            {
                var body = new JsonObject { ["tabelas"] = Clonar(headers), ["itens"] = chunk };
                var data = await PostAsync(TablesSpec.SlugTabelasPreco, body, ct);
                headersGravados = (int?)data?["tabelas"]?["gravados"] ?? headersGravados;
                var it = data?["itens"];
                var grv = (int?)it?["gravados"] ?? 0;
                itensGravados += grv;
                orfaos += (int?)it?["orfaos"] ?? 0;
                onChunk?.Invoke(i, chunk.Count, grv);
            }
            catch (ApiException e)
            {
                erros.Add(new ErroLote(i, e.Message));
            }
        }
        return new ResultadoTabelaPreco(headersGravados, itensGravados, orfaos, erros);
    }

    // POST num endpoint de base. Devolve o objeto `data` da resposta.
    // 1 retry (5s) em rede/5xx/429. 401 -> ApiAuthException.
    private async Task<JsonNode?> PostAsync(string slug, JsonObject payload, CancellationToken ct)
    {
        var path = $"api/v1/integracao/base/{slug}";
        var corpo = payload.ToJsonString();

        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
                };
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = e;
                if (attempt == 0) { await Task.Delay(RetryDelayMs, ct); continue; }
                throw new ApiException($"Falha de rede em {slug}: {e.Message}");
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new ApiAuthException($"401 em {slug}: chave invalida ou revogada");

            if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var txt = await SafeRead(resp);
                last = new ApiException($"HTTP {(int)resp.StatusCode} em {slug}: {Trunc(txt, 200)}", (int)resp.StatusCode);
                if (attempt == 0) { await Task.Delay(RetryDelayMs, ct); continue; }
                throw last;
            }

            var conteudo = await SafeRead(resp);
            JsonNode? body = null;
            if (!string.IsNullOrEmpty(conteudo))
            {
                // Resposta nao-JSON (ex.: 404 HTML do Next quando o slug nao existe) nao
                // pode derrubar o processo â€” vira ApiException com o texto truncado.
                try { body = JsonNode.Parse(conteudo); }
                catch (System.Text.Json.JsonException)
                {
                    throw new ApiException($"HTTP {(int)resp.StatusCode} em {slug}: resposta nao-JSON: {Trunc(conteudo, 200)}",
                        (int)resp.StatusCode);
                }
            }

            if ((int)resp.StatusCode >= 400)
            {
                var msg = body?["error"]?["message"]?.ToString();
                throw new ApiException($"HTTP {(int)resp.StatusCode} em {slug}: {(string.IsNullOrEmpty(msg) ? Trunc(conteudo, 200) : msg)}",
                    (int)resp.StatusCode);
            }
            return body?["data"];
        }
        throw new ApiException($"Falha em {slug}: {last?.Message}");
    }

    // JsonNode nao aceita dois pais: fatia/clona os nos para montar o corpo do chunk.
    private static JsonArray Fatiar(JsonArray origem, int inicio, int tamanho)
    {
        var saida = new JsonArray();
        var fim = Math.Min(inicio + tamanho, origem.Count);
        for (var i = inicio; i < fim; i++)
            saida.Add(origem[i]?.DeepClone());
        return saida;
    }

    private static JsonArray Clonar(JsonArray origem) => Fatiar(origem, 0, origem.Count);

    private static async Task<string> SafeRead(HttpResponseMessage resp)
    {
        try { return await resp.Content.ReadAsStringAsync(); }
        catch { return ""; }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
