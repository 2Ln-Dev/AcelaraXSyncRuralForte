using System.Text;

namespace AceleraXSync.Core.Security;

/// <summary>
/// Porte EXATO da cifra do ERP (unit Delphi criptografia.pas: Encrypta/Desencrypta/HexToInt),
/// portado verbatim do HubPadrao (2Ln-Dev/HubPadrao). O ERP guarda a senha de cada schema
/// cifrada em CADUSU.SENH_USU com esta cifra; usamos Desencrypta p/ recuperar a senha do
/// schema em runtime (modo ponte), sem depender de nada novo no banco do cliente.
///
/// Compat total com o Delphi: mesma chave/digito/logica (indexacao 1-based emulada).
/// Validado por round-trip e pelo caso conhecido: Encrypta('X') = '0000090'.
/// </summary>
public static class ErpCrypto
{
    // Parametros do ERP (Constantes.pas: CHAVE / DIGITO).
    public const string ChavePadrao = "a897v784";
    public const int DigitoPadrao = 7;

    /// <summary>Decifra o valor vindo de CADUSU.SENH_USU (Delphi Desencrypta).</summary>
    public static string Desencrypta(string? str, string chave = ChavePadrao, int dig = DigitoPadrao)
    {
        str = (str ?? "").Trim();
        if (dig < 2) dig = 2;            // igual ao Delphi
        if (chave.Length == 0) return "";

        var sb = new StringBuilder();
        var grupos = str.Length / dig;
        for (var g = 1; g <= grupos; g++)  // g = iStr do Delphi (1-based)
        {
            var tmp = str.Substring((g - 1) * dig, dig);
            var ich = (g % chave.Length) + 1;          // 1-based no Delphi
            var subOrd = HexToInt(tmp) - chave[ich - 1]; // -1 => 0-based em C#
            sb.Append((char)subOrd);
        }
        return sb.ToString();
    }

    /// <summary>Cifra (Delphi Encrypta). So' usado em teste/round-trip; o app decifra.</summary>
    public static string Encrypta(string? str, string chave = ChavePadrao, int dig = DigitoPadrao)
    {
        str = (str ?? "").Trim();
        if (chave.Length == 0) return "";

        var sb = new StringBuilder();
        for (var i = 1; i <= str.Length; i++)   // i = iStr do Delphi (1-based)
        {
            var ich = (i % chave.Length) + 1;
            var sumOrd = str[i - 1] + chave[ich - 1];
            sb.Append(sumOrd.ToString("X").PadLeft(dig, '0')); // = IntToHex(SumOrd, Dig)
        }
        return sb.ToString();
    }

    // IntToHex do Delphi produz maiusculas; Convert aceita maiusc/minusc.
    private static int HexToInt(string hex) => Convert.ToInt32(hex.Trim(), 16);
}
