namespace PedidoErp.Service.Core.Security;

/// <summary>
/// Credenciais do USUARIO-PONTE do ERP — o usuario fixo (igual em todo cliente 2LN) que
/// serve APENAS para ler CADUSU e recuperar a senha do schema (ver OracleConnectionFactory).
/// Portado verbatim do HubPadrao (2Ln-Dev/HubPadrao); mesmo ERP (SIAGRI/AGB) que a DLL usa,
/// mesma passphrase do <see cref="SecretProtector"/> — os blobs abaixo sao reutilizados.
///
/// Seguranca:
///  - NAO existem em texto puro no fonte/binario — so' como blob cifrado (AES-256-GCM);
///  - decifradas APENAS em memoria, no momento de abrir a conexao-ponte, e descartadas em
///    seguida (nao viram campo, propriedade publica nem singleton);
///  - nunca vao para arquivo, log ou mensagem de erro.
///
/// Regenerar (ao derivar outro usuario-ponte): `lic cripto "&lt;valor&gt;"` (repo 2Ln-Dev/license)
/// com a MESMA passphrase do SecretProtector; cole os "enc:v1:..." abaixo.
/// </summary>
internal static class BridgeCredentials
{
    // enc:v1:Base64(nonce|tag|ciphertext) — gerado por SecretProtector.Protect.
    private const string UsuarioEnc = "enc:v1:/k5Lqtea5hi6+2ZZFUxHuuOFX1wlOyRaH6IPBqewL/40fQ==";
    private const string SenhaEnc = "enc:v1:NXweglKXqT0P6g1G5TKA9a2rI3DKdxnMyp8te/M9QFgFmYwM";

    /// <summary>
    /// Decifra o par usuario/senha da ponte EM MEMORIA. Use imediatamente e descarte —
    /// nao guarde o retorno em campo de longa vida.
    /// </summary>
    public static (string Usuario, string Senha) Abrir() =>
        (SecretProtector.Unprotect(UsuarioEnc), SecretProtector.Unprotect(SenhaEnc));
}
