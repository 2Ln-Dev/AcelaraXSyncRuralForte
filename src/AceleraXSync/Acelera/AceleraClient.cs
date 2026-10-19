using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using AceleraXSync.Core;

namespace AceleraXSync.Acelera;

/// <summary>Chave invalida/revogada (HTTP 401) â€” interrompe o servico.</summary>
public sealed class ApiAuthException : Exception
{
    public ApiAuthException(string msg) : base(msg) { }
}

/// <summary>Falha num request (nao-auth). `Transitorio` separa rede/5xx/429 (retenta)
/// de 4xx (permanente/problema no pedido).</summary>
public sealed class ApiException : Exception
{
    /// <summary>HTTP status que originou o erro (null em falha de rede).</summary>
    public int? StatusCode { get; }
    /// <summary>true = vale retentar (rede/5xx/429); false = permanente (4xx != 401).</summary>
    public bool Transitorio { get; }

    public ApiException(string msg, int? statusCode = null, bool transitorio = true) : base(msg)
    {
        StatusCode = statusCode;
        Transitorio = transitorio;
    }
}

/// <summary>Um pedido a integrar (par pedido, origem) devolvido pela lista.
/// `TipoPed` so' vem preenchido na fila de ANALISE ('R'/'D'); na fila de
/// sincronizacao e' null (la' todos sao barter por construcao).</summary>
public sealed record PedidoResumo(string CodiPed, string? Origem, string? TipoPed = null);

/// <summary>Pedido ja' integrado (GET /pedidos/integrados): chave do pedido no ERP
/// (CODI_EMP + SERI_PED + PEDI_PED) e a situacao que o AceleraX ja' tem gravada.</summary>
public sealed record PedidoIntegrado(string CodiPed, long NumeroErp, long? Empresa, string Serie, string? Situacao);

/// <summary>Situacao do pedido no ERP para o POST /pedidos/situacao.</summary>
public sealed record SituacaoPedido(string CodiPed, string Situacao, string? Descricao);

/// <summary>Detalhe de UMA tentativa para o historico do AceleraX (pedidos_integracao_log):
/// JSON enviado a DLL e resposta crua. Vai junto em /integrar, /erro e /log.</summary>
public sealed record Tentativa(JsonObject? Payload, string? Resposta)
{
    public void Preencher(JsonObject body)
    {
        if (Payload is not null) body["payload"] = JsonNode.Parse(Payload.ToJsonString());
        if (!string.IsNullOrEmpty(Resposta)) body["resposta"] = Resposta.Length <= 50000 ? Resposta : Resposta[..50000];
    }
}

/// <summary>
/// Cliente da API de integracao de pedidos do AceleraX. Port de
/// pedido_erp/api_client.py. Autenticado por X-Api-Key; o tenant e' resolvido no
/// servidor pela chave. 1 retry (5s) em rede/5xx/429; 401 -> ApiAuthException.
/// </summary>
public sealed class AceleraClient
{
    private const int RetryDelayMs = 5000;

    private readonly HttpClient _http;

    public AceleraClient(HttpClient http, IOptions<AceleraOptions> opt)
    {
        var o = opt.Value;
        _http = http;
        // Config ausente/invalida deve dar erro CLARO (nao "Invalid URI" cru vindo do
        // new Uri). Ex.: appsettings.Production.json sem Acelera:ApiUrl.
        if (string.IsNullOrWhiteSpace(o.ApiUrl)
            || !Uri.TryCreate(o.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Acelera:ApiUrl invalido ou nao configurado: '{o.ApiUrl}'. Informe a URL base da API " +
                "do AceleraX (ex.: https://app.acelerax.com.br/api/v1) no appsettings.Production.json.");
        if (string.IsNullOrWhiteSpace(o.ApiKey))
            throw new InvalidOperationException(
                "Acelera:ApiKey nao configurada. Informe a X-Api-Key da integracao no appsettings.Production.json.");
        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        _http.DefaultRequestHeaders.Add("X-Api-Key", o.ApiKey);
    }

    /// <summary>Pares (pedido, origem) aprovados e ainda nao integrados.</summary>
    public Task<IReadOnlyList<PedidoResumo>> ListarIntegraveisAsync(string? desde, CancellationToken ct)
        => ListarAsync("api/v1/integracao/pedidos", desde, ct);

    private async Task<IReadOnlyList<PedidoResumo>> ListarAsync(string path, string? desde, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(desde))
            path += "?desde=" + Uri.EscapeDataString(desde);

        var data = await SendAsync(HttpMethod.Get, path, null, ct);
        var items = data?["items"]?.AsArray();
        if (items is null) return Array.Empty<PedidoResumo>();

        var lista = new List<PedidoResumo>(items.Count);
        foreach (var it in items)
        {
            if (it is null) continue;
            var codi = it["codi_ped"]?.ToString();
            if (string.IsNullOrEmpty(codi)) continue;
            var origem = it["origem"]?.ToString();
            var tipo = it["tipo_ped"]?.ToString();
            lista.Add(new PedidoResumo(codi,
                string.IsNullOrEmpty(origem) ? null : origem,
                string.IsNullOrEmpty(tipo) ? null : tipo));
        }
        return lista;
    }

    /// <summary>Detalhe do pedido; devolve o payloadErp (JSON pronto p/ a DLL) ou null.</summary>
    public async Task<JsonObject?> ObterPayloadAsync(string codiPed, string? origem, CancellationToken ct)
    {
        var path = $"api/v1/integracao/pedidos/{Uri.EscapeDataString(codiPed)}";
        if (!string.IsNullOrWhiteSpace(origem))
            path += "?origem=" + Uri.EscapeDataString(origem);

        var data = await SendAsync(HttpMethod.Get, path, null, ct);
        var payload = data?["payloadErp"];
        if (payload is null) return null;
        // Desanexa do no' pai (JsonNode nao pode ter dois pais) reparseando.
        return JsonNode.Parse(payload.ToJsonString())?.AsObject();
    }

    /// <summary>Marca o pedido como integrado. Idempotente no servidor.</summary>
    public async Task MarcarIntegradoAsync(string codiPed, string? numeroErp, string? origem, CancellationToken ct,
        Tentativa? tentativa = null)
    {
        var path = $"api/v1/integracao/pedidos/{Uri.EscapeDataString(codiPed)}/integrar";
        var body = new JsonObject { ["numero_erp"] = numeroErp };
        if (!string.IsNullOrWhiteSpace(origem)) body["origem"] = origem;
        tentativa?.Preencher(body);
        await SendAsync(HttpMethod.Post, path, body, ct);
    }

    /// <summary>Marca o pedido como ERRO PERMANENTE (POST /erro). Idempotente no servidor;
    /// no-op se ja integrado. `tipoErro` deve estar no enum aceito pelo endpoint.</summary>
    public async Task MarcarErroAsync(string codiPed, string tipoErro, string? mensagem, CancellationToken ct,
        Tentativa? tentativa = null)
    {
        var path = $"api/v1/integracao/pedidos/{Uri.EscapeDataString(codiPed)}/erro";
        var body = new JsonObject { ["tipo_erro"] = tipoErro, ["mensagem"] = mensagem };
        tentativa?.Preencher(body);
        await SendAsync(HttpMethod.Post, path, body, ct);
    }

    /// <summary>Pedidos ja' integrados ao ERP (GET /pedidos/integrados), com a chave no ERP.
    /// <paramref name="dias"/> limita pela data da integracao.</summary>
    public async Task<IReadOnlyList<PedidoIntegrado>> ListarIntegradosAsync(int? dias, CancellationToken ct)
    {
        var path = "api/v1/integracao/pedidos/integrados";
        if (dias is > 0) path += "?dias=" + dias.Value;
        var data = await SendAsync(HttpMethod.Get, path, null, ct);

        var lista = new List<PedidoIntegrado>();
        foreach (var it in data?["items"]?.AsArray() ?? new JsonArray())
        {
            var codi = it?["codi_ped"]?.ToString();
            if (string.IsNullOrEmpty(codi)
                || !long.TryParse(it!["numero_erp"]?.ToString(), out var numero)) continue;
            long? emp = long.TryParse(it["codigo_empresa"]?.ToString(), out var e) ? e : null;
            var situ = it["situacao"]?.ToString();
            lista.Add(new PedidoIntegrado(codi, numero, emp, it["serie"]?.ToString() ?? "1",
                string.IsNullOrWhiteSpace(situ) ? null : situ.Trim()));
        }
        return lista;
    }

    /// <summary>Grava a situacao dos pedidos no ERP (POST /pedidos/situacao), ate' 500 por chamada.
    /// Devolve quantos o AceleraX atualizou.</summary>
    public async Task<int> EnviarSituacaoAsync(IReadOnlyList<SituacaoPedido> itens, CancellationToken ct)
    {
        var total = 0;
        foreach (var lote in itens.Chunk(500))
        {
            var arr = new JsonArray();
            foreach (var s in lote)
                arr.Add(new JsonObject { ["codi_ped"] = s.CodiPed, ["situacao"] = s.Situacao, ["descricao"] = s.Descricao });
            var data = await SendAsync(HttpMethod.Post, "api/v1/integracao/pedidos/situacao", new JsonObject { ["itens"] = arr }, ct);
            total += int.TryParse(data?["atualizados"]?.ToString(), out var n) ? n : 0;
        }
        return total;
    }

    /// <summary>Agenda da sincronizacao por entidade (GET /agenda), configurada na tela do AceleraX.</summary>
    public async Task<IReadOnlyList<AgendaItem>> ObterAgendaAsync(CancellationToken ct)
    {
        var data = await SendAsync(HttpMethod.Get, "api/v1/integracao/agenda", null, ct);
        return (data?["items"]?.AsArray() ?? new JsonArray())
            .Select(AgendaService.DeJson)
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();
    }

    /// <summary>Devolve uma execucao de entidade (ultima execucao na tela de agenda).</summary>
    public async Task RegistrarExecucaoAsync(string entidade, Execucao ex, CancellationToken ct)
    {
        var path = $"api/v1/integracao/agenda/{Uri.EscapeDataString(entidade)}/execucao";
        var body = new JsonObject
        {
            ["inicio"] = ex.Inicio.ToString("o"),
            ["fim"] = ex.Fim.ToString("o"),
            ["resultado"] = ex.Resultado,
            ["mensagem"] = ex.Mensagem is { Length: > 4000 } m ? m[..4000] : ex.Mensagem,
            ["quantidade"] = ex.Quantidade,
            ["forcado"] = ex.Forcado,
        };
        await SendAsync(HttpMethod.Post, path, body, ct);
    }

    /// <summary>Registra no HISTORICO (POST /log) uma tentativa TRANSITORIA — ERP fora do
    /// ar, DLL sem conexao, rede. Nao muda o estado do pedido (segue pendente).</summary>
    public async Task RegistrarLogAsync(string codiPed, string? tipoErro, string? mensagem, CancellationToken ct,
        Tentativa? tentativa = null)
    {
        var path = $"api/v1/integracao/pedidos/{Uri.EscapeDataString(codiPed)}/log";
        var body = new JsonObject { ["tipo_erro"] = tipoErro, ["mensagem"] = mensagem };
        tentativa?.Preencher(body);
        await SendAsync(HttpMethod.Post, path, body, ct);
    }

    // Devolve o objeto `data` da resposta padrao { data }. 1 retry (5s) em rede/5xx/429.
    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? json, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(method, path);
                if (json is not null)
                    req.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = e;
                if (attempt == 0) { await Task.Delay(RetryDelayMs, ct); continue; }
                throw new ApiException($"Falha de rede em {method} {path}: {e.Message}");
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new ApiAuthException($"401 em {path}: chave invalida ou revogada");

            if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var txt = await SafeRead(resp);
                last = new ApiException($"HTTP {(int)resp.StatusCode} em {path}: {Trunc(txt, 200)}",
                    (int)resp.StatusCode, transitorio: true);
                if (attempt == 0) { await Task.Delay(RetryDelayMs, ct); continue; }
                throw last;
            }

            var content = await SafeRead(resp);
            JsonNode? body = string.IsNullOrEmpty(content) ? null : JsonNode.Parse(content);
            if ((int)resp.StatusCode >= 400)
            {
                var msg = body?["error"]?["message"]?.ToString();
                // 4xx (!=401) = problema no pedido/dados => PERMANENTE.
                throw new ApiException($"HTTP {(int)resp.StatusCode} em {path}: {(string.IsNullOrEmpty(msg) ? Trunc(content, 200) : msg)}",
                    (int)resp.StatusCode, transitorio: false);
            }
            return body?["data"];
        }
        throw new ApiException($"Falha em {method} {path}: {last?.Message}");
    }

    private static async Task<string> SafeRead(HttpResponseMessage resp)
    {
        try { return await resp.Content.ReadAsStringAsync(); }
        catch { return ""; }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
