using System.Globalization;

namespace AceleraXSync.Core.Data;

/// <summary>
/// Helpers para ler as linhas dinamicas do Dapper (cada linha e um
/// IDictionary&lt;string, object&gt;). Centraliza a coercao de tipos vindos do
/// Oracle â€” NUMBER chega como decimal (ou long, quando inteiro), DATE como
/// DateTime, CLOB como string â€” e o tratamento de nulos.
///
/// Mesmo arquivo do HubUniSeeds/HubPadrao (Core/Data/Rows.cs): a leitura do
/// banco segue o mesmo padrao em todos os servicos .NET 2LN.
/// </summary>
public static class Rows
{
    /// <summary>Converte a linha dinamica do Dapper em dicionario (case-insensitive por coluna).</summary>
    public static IDictionary<string, object?> D(dynamic row)
    {
        var src = (IDictionary<string, object>)row;
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in src)
            d[kv.Key] = kv.Value is DBNull ? null : kv.Value;
        return d;
    }

    private static object? Raw(IDictionary<string, object?> d, string col) =>
        d.TryGetValue(col, out var v) ? v : null;

    public static string? Str(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        return v is null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static long? Long(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        return v is null ? null : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    }

    public static int? Int(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        return v is null ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    public static decimal? Dec(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        return v is null ? null : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    public static DateTime? Date(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        return v is null ? null : Convert.ToDateTime(v, CultureInfo.InvariantCulture);
    }

    /// <summary>Colunas que guardam booleano como 0/1 ou 'S'/'N'/'Y'.</summary>
    public static bool Bool01(IDictionary<string, object?> d, string col)
    {
        var v = Raw(d, col);
        if (v is null) return false;
        if (v is bool b) return b;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().ToUpperInvariant();
        return s is "1" or "S" or "Y" or "T" or "TRUE";
    }
}
