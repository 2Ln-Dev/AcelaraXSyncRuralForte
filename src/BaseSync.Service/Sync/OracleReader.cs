using BaseSync.Service.Core.Data;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace BaseSync.Service.Sync;

/// <summary>Falha de leitura no Oracle apos o retry — o sync pula a entidade e segue.
/// Espelha OracleConnectionError do oracle_reader.py.</summary>
public sealed class OracleReadException : Exception
{
    public OracleReadException(string msg, Exception? inner = null) : base(msg, inner) { }
}

/// <summary>
/// Le uma entidade da view correspondente. Port de oracle_reader.py: monta
/// <c>SELECT &lt;colunas do spec&gt; FROM &lt;view&gt;</c> (sem WHERE, full sync) e devolve as
/// linhas como dicionarios coluna-Oracle -> valor. 1 retry (5s) em erro de banco.
///
/// Usa OracleDataReader cru (e nao Dapper como o PedidoErp.Service) porque aqui as
/// colunas sao DINAMICAS (vem do spec) e precisamos do tipo nativo de cada valor para
/// reproduzir as conversoes do transformer.py.
/// </summary>
public sealed class OracleReader
{
    private const int RetryDelayMs = 5000;

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OracleReader> _log;

    public OracleReader(IDbConnectionFactory db, ILogger<OracleReader> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<List<Dictionary<string, object?>>> FetchAsync(string entidade, CancellationToken ct)
    {
        var spec = TablesSpec.Entidades[entidade];
        var colunas = spec.Columns.Select(c => c.Key).ToArray();
        var sql = $"SELECT {string.Join(", ", colunas)} FROM {spec.OracleView}";

        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await LerAsync(sql, colunas, ct);
            }
            catch (OracleException e)
            {
                last = e;
                if (attempt == 0)
                {
                    _log.LogWarning(e, "Falha lendo {Entidade} ({View}) — retry em {Delay}s",
                        entidade, spec.OracleView, RetryDelayMs / 1000);
                    await Task.Delay(RetryDelayMs, ct);
                }
            }
        }
        throw new OracleReadException($"Oracle fetch falhou para {entidade}: {last?.Message}", last);
    }

    private async Task<List<Dictionary<string, object?>>> LerAsync(
        string sql, string[] colunas, CancellationToken ct)
    {
        await using var conn = await _db.CreateOpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        // Menos round-trips nas views grandes (carteira/itens de tabela de preco).
        cmd.FetchSize = cmd.FetchSize * 8;

        await using var rd = (OracleDataReader)await cmd.ExecuteReaderAsync(ct);

        // Colunas NUMBER: ler SEMPRE como decimal (GetOracleDecimal), nunca via GetValue.
        // O ODP.NET mapeia NUMBER de baixa precisao para Single — NUMBER(5,2) = 1.16 viraria
        // 1.1599999666... ao virar double, divergindo do Python (cx_Oracle entrega double).
        var numerica = new bool[colunas.Length];
        for (var i = 0; i < colunas.Length; i++)
            numerica[i] = rd.GetProviderSpecificFieldType(i) == typeof(OracleDecimal);

        var linhas = new List<Dictionary<string, object?>>();
        while (await rd.ReadAsync(ct))
        {
            var linha = new Dictionary<string, object?>(colunas.Length, StringComparer.Ordinal);
            for (var i = 0; i < colunas.Length; i++)
                linha[colunas[i]] = rd.IsDBNull(i) ? null : LerValor(rd, i, numerica[i]);
            linhas.Add(linha);
        }
        return linhas;
    }

    private static object LerValor(OracleDataReader rd, int i, bool numerica)
    {
        if (!numerica) return rd.GetValue(i);
        var n = rd.GetOracleDecimal(i);
        // NUMBER fora da faixa do decimal (raro no ERP): cai para double, que ainda cobre
        // a magnitude, em vez de estourar a leitura inteira da entidade.
        try { return n.Value; }
        catch (OverflowException) { return n.ToDouble(); }
    }
}
