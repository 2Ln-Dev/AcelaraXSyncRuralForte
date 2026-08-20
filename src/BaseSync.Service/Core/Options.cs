namespace BaseSync.Service.Core;

/// <summary>API do AceleraX (X-Api-Key). Espelha ACELERA_API_* do .env Python.</summary>
public sealed class AceleraOptions
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Conexao Oracle (driver gerenciado). Dois modos (ver OracleConnectionFactory):
///  - DIRETO (dev): User + Password (ou PasswordEnc). Conecta direto.
///  - PONTE (cliente): so' Schema + Dsn (sem User). A senha do schema vem do CADUSU do ERP.
/// O Dsn e' SEMPRE explicito (EZConnect host:porta/servico) — o driver gerenciado nao
/// consulta a variavel de ambiente ORACLE_SID da maquina (que aqui e' XE, do Oracle local).
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

/// <summary>
/// Parametros do sincronizador de base. Espelha o comportamento do sync.py:
/// one-shot por padrao (RunOnce=true); loop opcional a cada IntervalSeconds
/// (SYNC_INTERVAL_SECONDS do .env Python).
/// </summary>
public sealed class SyncOptions
{
    /// <summary>Registros por chunk enviado a API (BATCH_SIZE do Python; a API aceita ate 5000).</summary>
    public int BatchSize { get; set; } = 1000;
    /// <summary>true = UMA rodada e encerra (igual ao sync.py). false = loop a cada IntervalSeconds.</summary>
    public bool RunOnce { get; set; } = true;
    /// <summary>Intervalo do modo loop (SYNC_INTERVAL_SECONDS do Python).</summary>
    public int IntervalSeconds { get; set; } = 3600;
    /// <summary>Teste: le e transforma TUDO mas NAO chama a API — grava o payload
    /// transformado de cada entidade em DumpDir/&lt;entidade&gt;.json (base da comparacao
    /// Python x .NET).</summary>
    public bool DryRun { get; set; }
    /// <summary>Pasta dos dumps do DryRun (relativa ao exe).</summary>
    public string DumpDir { get; set; } = "dump";
    /// <summary>Teste: processa somente esta entidade (nome do tables_spec, ex.: "empresa").</summary>
    public string? Somente { get; set; }
    /// <summary>Teste: maximo de linhas por entidade apos a transformacao (carga controlada).</summary>
    public int? Limite { get; set; }
}
