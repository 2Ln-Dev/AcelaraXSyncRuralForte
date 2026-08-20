namespace PedidoErp.Service.Core;

/// <summary>API do AceleraX (X-Api-Key). Espelha ACELERA_API_* do .env Python.</summary>
public sealed class AceleraOptions
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Conexao Oracle (driver gerenciado). Dois modos (ver OracleConnectionFactory):
///  - DIRETO (dev): User + Password (ou PasswordEnc). Conecta direto — modo testado.
///  - PONTE (cliente): so' Schema + Dsn (sem User). A senha do schema vem do CADUSU do ERP.
/// </summary>
public sealed class OracleOptions
{
    public string User { get; set; } = "";
    /// <summary>Senha em claro (modo direto/dev). Selada para PasswordEnc no 1o boot.</summary>
    public string Password { get; set; } = "";
    /// <summary>Senha cifrada (enc:v1:...); prioridade sobre Password. So' modo direto.</summary>
    public string PasswordEnc { get; set; } = "";
    public string Dsn { get; set; } = "";
    public int PoolMin { get; set; } = 2;
    public int PoolMax { get; set; } = 10;
    public string? Schema { get; set; } = "AGROINSU";
}

/// <summary>DLL 32-bit AGBPedidoVenda. Espelha AGB_* do .env Python.</summary>
public sealed class DllOptions
{
    public string DllPath { get; set; } = "agb/AGBPedidoVenda.dll";
    public string? AgbDir { get; set; }
    /// <summary>Codepage ANSI que a DLL espera no PAnsiChar (Brasil = 1252).</summary>
    public int Encoding { get; set; } = 1252;
}

/// <summary>Parametros do worker de pedido. Espelha PEDIDO_* do .env Python.</summary>
public sealed class PedidoOptions
{
    public int PollSeconds { get; set; } = 300;
    public string? Desde { get; set; }
    public int? Limite { get; set; }
    /// <summary>true = processa UMA rodada e encerra (one-shot p/ Task Scheduler e teste).
    /// false = loop a cada PollSeconds (modo servico continuo).</summary>
    public bool RunOnce { get; set; }
    /// <summary>Teste: materializa a tabela (gerar-tabela) mas NAO chama a DLL nem marca
    /// integrado. Serve p/ validar a geracao 3b sem gravar pedido no ERP.</summary>
    public bool SkipDll { get; set; }
    /// <summary>Teste: processa somente este codi_ped (mira 1 pedido, como o --pedido do Python).</summary>
    public string? Somente { get; set; }
}
