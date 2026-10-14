using System.Globalization;
using System.Text.Json.Nodes;
using AceleraXSync.Acelera;
using Microsoft.Extensions.Logging;

namespace AceleraXSync.Core;

/// <summary>Configuracao de UMA entidade na agenda do AceleraX (tela Integracao ERP ›
/// Agenda de sincronizacao). Modo INTERVALO (a cada Minutos), HORARIO (todo dia as
/// Horario, hora local do servidor) ou DESATIVADO. ForcadoEm = "Forcar agora" pendente.</summary>
public sealed record AgendaItem(
    string Entidade, string Modo, int? Minutos, string? Horario,
    DateTimeOffset? ForcadoEm, DateTimeOffset? UltimaExecucao);

/// <summary>Uma execucao de entidade, devolvida ao AceleraX (ultima execucao na tela).</summary>
public sealed record Execucao(
    DateTimeOffset Inicio, DateTimeOffset Fim, string Resultado, string? Mensagem, int? Quantidade, bool Forcado);

/// <summary>
/// Agenda da sincronizacao, lida do AceleraX (GET /api/v1/integracao/agenda) e
/// compartilhada pelos dois workers. Decide o que esta' "na hora" e devolve cada
/// execucao (POST .../agenda/:entidade/execucao).
///
/// Robustez: se a API falhar, usa a ultima agenda lida; se NUNCA leu (app sem a
/// agenda ainda), ObterAsync devolve null e o worker cai no intervalo fixo do
/// appsettings. Execucoes ficam lembradas localmente, entao uma falha ao registrar
/// nao faz a entidade rodar de novo a cada minuto.
/// </summary>
public sealed class AgendaService
{
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(30);

    private readonly AceleraClient _api;
    private readonly ILogger<AgendaService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, AgendaItem>? _agenda;
    private DateTimeOffset _lidaEm;
    private readonly Dictionary<string, DateTimeOffset> _ultimaLocal = new();
    private readonly Dictionary<string, DateTimeOffset> _forcadoAtendido = new();
    private bool _avisouIndisponivel;
    private readonly DateTimeOffset _inicio = DateTimeOffset.Now;

    public AgendaService(AceleraClient api, ILogger<AgendaService> log)
    {
        _api = api;
        _log = log;
    }

    /// <summary>Agenda atual (cache de 30s). null = nunca conseguiu ler (usar o intervalo fixo).</summary>
    public async Task<IReadOnlyDictionary<string, AgendaItem>?> ObterAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_agenda is not null && DateTimeOffset.UtcNow - _lidaEm < Validade) return _agenda;
            try
            {
                var itens = await _api.ObterAgendaAsync(ct);
                _agenda = itens.ToDictionary(i => i.Entidade, StringComparer.Ordinal);
                _lidaEm = DateTimeOffset.UtcNow;
                if (_avisouIndisponivel) _log.LogInformation("Agenda do AceleraX disponivel novamente.");
                _avisouIndisponivel = false;
            }
            catch (ApiAuthException) { throw; }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (!_avisouIndisponivel)
                    _log.LogWarning("Agenda do AceleraX indisponivel ({Msg}) - usando {Fonte}.",
                        e.Message, _agenda is null ? "o intervalo fixo do appsettings" : "a ultima agenda lida");
                _avisouIndisponivel = true;
            }
            return _agenda;
        }
        finally { _lock.Release(); }
    }

    /// <summary>true se a entidade deve rodar agora (forcada ou na hora pela agenda).</summary>
    public bool Devida(AgendaItem item, DateTimeOffset agora, out bool forcada)
    {
        forcada = item.ForcadoEm is { } f
                  && !(_forcadoAtendido.TryGetValue(item.Entidade, out var atendido) && atendido >= f);
        if (forcada) return true;

        var ultima = Ultima(item);
        switch (item.Modo)
        {
            case "INTERVALO":
                var minutos = Math.Max(1, item.Minutos ?? 60);
                return ultima is null || agora - ultima.Value >= TimeSpan.FromMinutes(minutos);
            case "HORARIO":
                if (!TimeSpan.TryParseExact(item.Horario, @"hh\:mm", CultureInfo.InvariantCulture, out var hora))
                    return false;
                var local = agora.ToLocalTime();
                var alvo = new DateTimeOffset(local.Date + hora, local.Offset);
                // Nunca rodou: espera o PROXIMO horario depois que o servico subiu (nao
                // dispara uma carga diaria pesada so' porque ja' passou das HH:MM de hoje).
                var referencia = ultima ?? _inicio;
                return local >= alvo && referencia < alvo;
            default:
                return false; // DESATIVADO (ou modo desconhecido): so' roda se forcado
        }
    }

    /// <summary>Lembra a execucao localmente e a devolve ao AceleraX (best-effort).</summary>
    public async Task RegistrarAsync(string entidade, AgendaItem? item, Execucao ex, CancellationToken ct)
    {
        _ultimaLocal[entidade] = ex.Inicio;
        if (ex.Forcado && item?.ForcadoEm is { } f) _forcadoAtendido[entidade] = f;
        try
        {
            await _api.RegistrarExecucaoAsync(entidade, ex, ct);
        }
        catch (ApiAuthException) { throw; }
        catch (Exception e)
        {
            _log.LogWarning("Falha ao registrar a execucao de {Entidade} no AceleraX: {Msg}", entidade, e.Message);
        }
    }

    private DateTimeOffset? Ultima(AgendaItem item)
    {
        var local = _ultimaLocal.TryGetValue(item.Entidade, out var l) ? l : (DateTimeOffset?)null;
        if (item.UltimaExecucao is null) return local;
        if (local is null) return item.UltimaExecucao;
        return item.UltimaExecucao > local ? item.UltimaExecucao : local;
    }

    /// <summary>Converte um item de GET /agenda (ver lib/services/integracao-agenda.ts).</summary>
    internal static AgendaItem? DeJson(JsonNode? n)
    {
        var entidade = n?["entidade"]?.ToString();
        if (string.IsNullOrEmpty(entidade)) return null;
        return new AgendaItem(
            entidade,
            n!["modo"]?.ToString() ?? "INTERVALO",
            n["minutos"] is JsonValue m && m.TryGetValue<int>(out var min) ? min : null,
            n["horario"]?.ToString(),
            Data(n["forcadoEm"]),
            Data(n["ultima"]?["inicio"]));
    }

    private static DateTimeOffset? Data(JsonNode? n) =>
        DateTimeOffset.TryParse(n?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
