using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AceleraXSync.Core.Security;

/// <summary>
/// Criptografia simetrica PROPRIA (AES-256-GCM) para segredos em disco. Portado verbatim
/// do HubPadrao (repo 2Ln-Dev/HubPadrao, Core/Security) â€” mesma Passphrase e formato, para
/// os blobs "enc:v1:..." serem compativeis entre os produtos 2LN (gerados por `lic cripto`).
///
/// A chave vem de <see cref="Passphrase"/> (SHA-256), portatil (nao presa a maquina). Protege
/// contra ler o arquivo, nao contra engenharia reversa do binario. Manter em sincronia com a
/// ferramenta de licenca (repo 2Ln-Dev/license).
/// </summary>
public static class SecretProtector
{
    private const string Passphrase = "2lnlamsts";
    private const string Prefixo = "enc:v1:"; // marca o formato do valor cifrado

    private static byte[] Chave() => SHA256.HashData(Encoding.UTF8.GetBytes(Passphrase));

    /// <summary>Cifra e retorna "enc:v1:" + Base64(nonce | tag | ciphertext).</summary>
    public static string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize); // 12
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16
        using (var aes = new AesGcm(Chave(), tag.Length))
            aes.Encrypt(nonce, pt, ct, tag);

        var pacote = new byte[nonce.Length + tag.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, pacote, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, pacote, nonce.Length, tag.Length);
        Buffer.BlockCopy(ct, 0, pacote, nonce.Length + tag.Length, ct.Length);
        return Prefixo + Convert.ToBase64String(pacote);
    }

    public static string Unprotect(string cifrado)
    {
        if (cifrado.StartsWith(Prefixo, StringComparison.Ordinal)) cifrado = cifrado[Prefixo.Length..];
        var pacote = Convert.FromBase64String(cifrado);
        var nonce = pacote[..12];
        var tag = pacote[12..28];
        var ct = pacote[28..];
        var pt = new byte[ct.Length];
        using (var aes = new AesGcm(Chave(), tag.Length))
            aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>Decifra a senha se vier cifrada (PasswordEnc); senao devolve a em claro.</summary>
    public static string ResolverSenhaOracle(string? password, string? passwordEnc) =>
        !string.IsNullOrEmpty(passwordEnc) ? Unprotect(passwordEnc) : (password ?? "");

    /// <summary>
    /// Se o appsettings.Production.json tiver Oracle:Password em CLARO, cifra em
    /// Oracle:PasswordEnc e REMOVE o Password. Idempotente â€” roda no boot (sela a senha na
    /// 1a execucao apos a instalacao). No modo ponte nao ha senha, entao e' no-op.
    /// </summary>
    public static void SelarSenhaOracle(string baseDir) => SelarSegredos(baseDir);

    /// <summary>
    /// Sela os segredos em CLARO do appsettings.Production.json: Oracle:Password ->
    /// PasswordEnc e Acelera:ApiUrl/ApiKey -> ApiUrlEnc/ApiKeyEnc (removendo os em claro).
    /// Idempotente; roda no boot, ANTES de carregar a configuracao. Nunca derruba o boot.
    /// </summary>
    public static void SelarSegredos(string baseDir)
    {
        var path = Path.Combine(baseDir, "appsettings.Production.json");
        try
        {
            if (!File.Exists(path)) return;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return;

            var mudou = Selar(root["Oracle"] as JsonObject, "Password", "PasswordEnc");
            mudou |= Selar(root["Acelera"] as JsonObject, "ApiUrl", "ApiUrlEnc");
            mudou |= Selar(root["Acelera"] as JsonObject, "ApiKey", "ApiKeyEnc");
            if (mudou)
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Selagem nunca derruba o boot.
        }
    }

    private static bool Selar(JsonObject? secao, string claro, string cifrado)
    {
        if (secao?[claro] is not JsonValue v || !v.TryGetValue<string>(out var valor) || string.IsNullOrEmpty(valor))
            return false; // nada em claro para selar
        secao[cifrado] = Protect(valor);
        secao.Remove(claro);
        return true;
    }

    /// <summary>Valor efetivo: o cifrado (se houver) tem prioridade sobre o em claro.</summary>
    public static string Resolver(string? claro, string? cifrado) =>
        !string.IsNullOrEmpty(cifrado) ? Unprotect(cifrado) : (claro ?? "");
}
