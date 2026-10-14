using AceleraXSync.Core;
using AceleraXSync.Core.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace AceleraXSync.Sync;

/// <summary>Falha de leitura no Oracle apos o retry â€” o sync pula a entidade e segue.</summary>
public sealed class OracleReadException : Exception
{
    public OracleReadException(string msg, Exception? inner = null) : base(msg, inner) { }
}

/// <summary>
/// Le uma entidade do ERP. Dois modos (Sync:ConsultasDir):
///
///  - VIEW (padrao, cliente original): monta
///    <c>SELECT &lt;colunas do spec&gt; FROM &lt;view&gt;</c> (sem WHERE, full sync).
///  - CONSULTA: executa a consulta &lt;entidade&gt;.sql do cliente (Sync:ConsultasDir, ex.:
///    "ruralforte"), EMBUTIDA no exe como recurso (consultas/&lt;cliente&gt;/*.sql no projeto)
///    — o SQL nao fica em disco no servidor do cliente.
///    Os APELIDOS das colunas sao o contrato: so' as colunas que o spec conhece sao
///    lidas, e coluna do spec AUSENTE da consulta nao vai no payload â€” o upsert nao a
///    toca (ex.: CODI_CIC omitido para preservar a safra definida no app).
///
/// LEITURA: Dapper (<c>QueryAsync</c>) + <see cref="Rows.D"/>, o mesmo padrao do
/// HubUniSeeds. Cada linha vem como dicionario dinamico coluna -> valor; NUMBER chega
/// como decimal (ou long, quando inteiro) e DATE como DateTime, tipos que o
/// <see cref="Transformer"/> converte para o JSON do payload. 1 retry (5s) em erro de banco.
/// </summary>
public sealed class OracleReader
{
    private const int RetryDelayMs = 5000;

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OracleReader> _log;
    /// <summary>entidade -> SQL (ja' sem terminador). null = modo VIEW.</summary>
    private readonly IReadOnlyDictionary<string, string>? _consultas;

    public OracleReader(IDbConnectionFactory db, IOptions<SyncOptions> opt, ILogger<OracleReader> log)
    {
        _db = db;
        _log = log;
        _consultas = CarregarConsultas(opt.Value.ConsultasDir);
    }

    /// <summary>true = le as consultas embutidas do cliente; false = le as views V_AX_*.</summary>
    public bool ModoConsulta => _consultas is not null;

    /// <summary>Descricao da origem da entidade para log ("arquivo.sql" ou a view), ou null
    /// se a entidade nao tem origem neste modo (sem arquivo / sem view) e deve ser pulada.</summary>
    public string? Origem(string entidade)
    {
        if (_consultas is null) return TablesSpec.Entidades[entidade].OracleView;
        return _consultas.ContainsKey(entidade) ? $"{entidade}.sql" : null;
    }

    /// <summary>Consultas do cliente que nao correspondem a nenhuma entidade (erro de
    /// digitacao no nome = entidade silenciosamente fora da carga).</summary>
    public IEnumerable<string> ArquivosSemEntidade() =>
        _consultas is null
            ? Enumerable.Empty<string>()
            : _consultas.Keys.Where(k => !TablesSpec.Entidades.ContainsKey(k)).Select(k => $"{k}.sql");

    public async Task<List<Dictionary<string, object?>>> FetchAsync(string entidade, CancellationToken ct)
    {
        var spec = TablesSpec.Entidades[entidade];
        string sql, origem;
        if (_consultas is null)
        {
            if (spec.OracleView is null)
                throw new InvalidOperationException($"Entidade {entidade} nao tem view; so' existe no modo consulta.");
            var colunas = spec.Columns.Where(c => !c.SoConsulta).Select(c => c.Oracle);
            sql = $"SELECT {string.Join(", ", colunas)} FROM {spec.OracleView}";
            origem = spec.OracleView;
        }
        else
        {
            origem = $"{entidade}.sql";
            sql = _consultas.TryGetValue(entidade, out var q)
                ? q
                : throw new InvalidOperationException($"Sem consulta para {entidade}.");
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await LerAsync(entidade, sql, ct);
            }
            catch (OracleException e)
            {
                last = e;
                if (attempt == 0)
                {
                    _log.LogWarning(e, "Falha lendo {Entidade} ({Origem}) â€” retry em {Delay}s",
                        entidade, origem, RetryDelayMs / 1000);
                    await Task.Delay(RetryDelayMs, ct);
                }
            }
        }
        throw new OracleReadException($"Oracle fetch falhou para {entidade}: {last?.Message}", last);
    }

    private async Task<List<Dictionary<string, object?>>> LerAsync(
        string entidade, string sql, CancellationToken ct)
    {
        await using var conn = await _db.CreateOpenAsync(ct);
        var rowsDyn = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));

        // Casa as colunas do resultado com o spec pelo NOME (apelido). No modo view o
        // SELECT ja' vem na ordem do spec; no modo consulta a ordem e' livre.
        var doSpec = TablesSpec.Entidades[entidade].Columns
            .Select(c => c.Oracle).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var linhas = new List<Dictionary<string, object?>>();
        var primeiro = true;
        foreach (var r in rowsDyn)
        {
            // Dapper entrega cada linha como IDictionary<string, object>; Rows.D normaliza
            // (case-insensitive, DBNull -> null), igual ao HubUniSeeds. Tipado explicito
            // porque Rows.D tem parametro dynamic (senao Keys/TryGetValue viram dynamic).
            IDictionary<string, object?> row = Rows.D(r);

            if (primeiro)
            {
                primeiro = false;
                var ignoradas = row.Keys.Where(k => !doSpec.Contains(k)).ToList();
                if (ignoradas.Count > 0)
                    _log.LogWarning("entidade={Entidade} colunas fora do contrato IGNORADAS: {Colunas}",
                        entidade, string.Join(", ", ignoradas));
                if (ModoConsulta)
                {
                    var ausentes = doSpec.Where(c => !row.ContainsKey(c)).ToList();
                    if (ausentes.Count > 0)
                        _log.LogInformation("entidade={Entidade} colunas do contrato fora da consulta (nao enviadas): {Colunas}",
                            entidade, string.Join(", ", ausentes));
                }
            }

            // So' as colunas do contrato vao para a linha; coluna ausente fica de fora
            // (o Transformer nao a envia e o upsert nao a toca).
            var linha = new Dictionary<string, object?>(doSpec.Count, StringComparer.Ordinal);
            foreach (var c in doSpec)
                if (row.TryGetValue(c, out var v))
                    linha[c] = v;
            linhas.Add(linha);
        }
        return linhas;
    }

    // --- Consultas embutidas -------------------------------------------------

    private const string PrefixoRecurso = "consultas/";

    /// <summary>Le as consultas do cliente embutidas no assembly. <paramref name="cliente"/>
    /// aceita "ruralforte" ou "consultas/ruralforte". Vazio = modo VIEW (null).</summary>
    private static IReadOnlyDictionary<string, string>? CarregarConsultas(string? cliente)
    {
        if (string.IsNullOrWhiteSpace(cliente)) return null;
        var nome = cliente.Trim().Replace('\\', '/').Trim('/');
        if (nome.StartsWith(PrefixoRecurso, StringComparison.OrdinalIgnoreCase)) nome = nome[PrefixoRecurso.Length..];
        var prefixo = $"{PrefixoRecurso}{nome}/";

        var asm = typeof(OracleReader).Assembly;
        var consultas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recurso in asm.GetManifestResourceNames())
        {
            var r = recurso.Replace('\\', '/');
            if (!r.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) || !r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;
            var entidade = r[prefixo.Length..^".sql".Length];
            if (entidade.Contains('/')) continue;
            using var st = asm.GetManifestResourceStream(recurso)!;
            using var rd = new StreamReader(st);
            consultas[entidade] = LimparConsulta(rd.ReadToEnd(), $"{nome}/{entidade}.sql");
        }
        if (consultas.Count == 0)
            throw new InvalidOperationException(
                $"Sync:ConsultasDir='{cliente}': nenhuma consulta embutida para o cliente '{nome}'. " +
                $"Clientes disponiveis: {string.Join(", ", ClientesEmbutidos(asm))}. " +
                "(ConsultasDir e' o nome do cliente das consultas, ex.: ruralforte - NAO o schema do Oracle.) " +
                "As consultas ficam em src/AceleraXSync/consultas/<cliente>/ e entram no exe no build.");
        return consultas;
    }

    /// <summary>Nomes dos clientes com consultas embutidas (para a mensagem de erro).</summary>
    private static IEnumerable<string> ClientesEmbutidos(System.Reflection.Assembly asm) =>
        asm.GetManifestResourceNames()
            .Select(r => r.Replace('\\', '/'))
            .Where(r => r.StartsWith(PrefixoRecurso, StringComparison.OrdinalIgnoreCase))
            .Select(r => r[PrefixoRecurso.Length..].Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n);

    /// <summary>Tira o terminador: o ODP.NET recusa ';' ou '/' no fim (ORA-00933/ORA-00911),
    /// mas o .sql precisa deles para rodar no SQL Developer.</summary>
    internal static string LimparConsulta(string texto, string nome)
    {
        var sql = texto.TrimEnd();
        while (sql.EndsWith(';') || sql.EndsWith('/'))
            sql = sql[..^1].TrimEnd();
        if (sql.Length == 0)
            throw new InvalidOperationException($"Consulta vazia: {nome}");
        return sql;
    }
}
