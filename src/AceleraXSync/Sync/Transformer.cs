using System.Globalization;
using System.Text.Json.Nodes;

namespace AceleraXSync.Sync;

/// <summary>
/// Aplica o De/Para do spec e as conversoes de tipo. Port FIEL de transformer.py:
///
///   None            -> null
///   datetime/date   -> ISO 8601 (isoformat)
///   float inteiro   -> int   (80.0 vira 80)
///   resto           -> valor original
///   coluna ausente  -> fora do payload (so' no modo consulta; ver OracleReader)
///
/// A saida e' o JSON exato que vai no "items" do POST â€” e' o que a comparacao
/// Python x .NET confere campo a campo.
/// </summary>
public static class Transformer
{
    public static JsonArray Transform(IEnumerable<Dictionary<string, object?>> linhas, string entidade)
    {
        var colunas = TablesSpec.Entidades[entidade].Columns;
        var saida = new JsonArray();
        foreach (var linha in linhas)
        {
            var obj = new JsonObject();
            foreach (var col in colunas)
            {
                // Coluna que a origem nao trouxe fica FORA do payload (nao vira null): o
                // upsert nao a toca. No modo view toda coluna do spec vem na linha.
                if (linha.TryGetValue(col.Oracle, out var bruto))
                    obj[col.Destino] = Converter(bruto);
            }
            saida.Add(obj);
        }
        return saida;
    }

    /// <summary>Espelha _convert_value() do transformer.py.</summary>
    public static JsonNode? Converter(object? valor) => valor switch
    {
        null or DBNull => null,

        // Oracle DATE/TIMESTAMP -> isoformat() do Python. O datetime.isoformat() omite
        // os microssegundos quando sao zero; reproduzimos isso para o JSON bater.
        DateTime dt => JsonValue.Create(FormatarData(dt)),

        // NUMBER: o driver devolve decimal. O Python recebe float e converte para int
        // quando o valor e' inteiro (value == int(value)) â€” mesma regra aqui, para o
        // JSON sair "80" e nao "80.0".
        decimal d => InteiroOuDecimal(d),
        double db => double.IsInteger(db) ? JsonValue.Create((long)db) : JsonValue.Create(db),
        float f => float.IsInteger(f) ? JsonValue.Create((long)f) : JsonValue.Create((double)f),

        // Tipos ja' inteiros/textuais passam direto (o Python tambem nao os toca).
        byte or sbyte or short or ushort or int or uint or long or ulong => JsonValue.Create(Convert.ToInt64(valor)),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),

        _ => JsonValue.Create(Convert.ToString(valor, CultureInfo.InvariantCulture)),
    };

    private static JsonNode InteiroOuDecimal(decimal d)
    {
        // decimal.Truncate == d  <=> nao ha parte fracionaria (equivalente ao
        // "value == int(value)" do Python). long cobre a faixa dos codigos/quantidades
        // do ERP; fora dela mantemos decimal para nao perder valor.
        if (decimal.Truncate(d) == d && d >= long.MinValue && d <= long.MaxValue)
            return JsonValue.Create((long)d);
        return JsonValue.Create(d);
    }

    private static string FormatarData(DateTime dt) =>
        dt.Ticks % TimeSpan.TicksPerSecond == 0
            ? dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture);
}
