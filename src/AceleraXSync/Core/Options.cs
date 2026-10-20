namespace AceleraXSync.Core;

/// <summary>API do AceleraX (X-Api-Key). Espelha ACELERA_API_* do .env Python.</summary>
public sealed class AceleraOptions
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>ApiUrl/ApiKey cifradas (enc:v1:..., SecretProtector) — prioridade sobre as
    /// em claro. O servico sela as em claro do appsettings.Production.json no boot.</summary>
    public string ApiUrlEnc { get; set; } = "";
    public string ApiKeyEnc { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Conexao Oracle (driver gerenciado). Dois modos (ver OracleConnectionFactory):
///  - DIRETO (dev): User + Password (ou PasswordEnc). Conecta direto.
///  - PONTE (cliente): so' Schema + Dsn (sem User). A senha do schema vem do CADUSU do ERP.
/// O Dsn e' SEMPRE explicito (EZConnect host:porta/servico) â€” o driver gerenciado nao
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
    /// <summary>Intervalo FIXO do modo loop — so' vale enquanto a agenda do AceleraX nao
    /// puder ser lida (tela Integracao ERP › Agenda de sincronizacao manda no resto).</summary>
    public int IntervalSeconds { get; set; } = 3600;
    /// <summary>De quanto em quanto tempo o servico confere a agenda do AceleraX.</summary>
    public int AgendaTickSeconds { get; set; } = 60;
    /// <summary>Teste: le e transforma TUDO mas NAO chama a API â€” grava o payload
    /// transformado de cada entidade em DumpDir/&lt;entidade&gt;.json (base da comparacao
    /// Python x .NET).</summary>
    public bool DryRun { get; set; }
    /// <summary>Pasta dos dumps do DryRun (relativa ao exe).</summary>
    public string DumpDir { get; set; } = "dump";
    /// <summary>Teste: processa somente esta entidade (nome do tables_spec, ex.: "empresa").</summary>
    public string? Somente { get; set; }
    /// <summary>Teste: maximo de linhas por entidade apos a transformacao (carga controlada).</summary>
    public int? Limite { get; set; }
    /// <summary>Pasta das consultas do cliente (relativa ao exe), ex.: "consultas/ruralforte".
    /// Vazio = modo VIEW (le as V_AX_* do cliente original). Preenchido = cada entidade le
    /// &lt;pasta&gt;/&lt;entidade&gt;.sql; entidade sem arquivo NAO e' sincronizada.</summary>
    public string? ConsultasDir { get; set; }
}

/// <summary>DLL 32-bit AGBPedidoVenda (gravacao de pedido no ERP).</summary>
public sealed class DllOptions
{
    public string DllPath { get; set; } = "agb/AGBPedidoVenda.dll";
    public string? AgbDir { get; set; }
    /// <summary>Codepage ANSI que a DLL espera no PAnsiChar (Brasil = 1252).</summary>
    public int Encoding { get; set; } = 1252;
    /// <summary>Pasta do Oracle client 32-bit do ERP (oci.dll, oraocci11.dll, tbodbxora.dll),
    /// posta no PATH do processo antes de carregar a DLL. Padrao: o do SIAGRI. Vazio = nao mexe.</summary>
    public string? OracleClientDir { get; set; } = @"C:\siagri\bin";
}

/// <summary>Parametros do worker de pedido (AceleraX -> ERP).</summary>
public sealed class PedidoOptions
{
    /// <summary>Intervalo FIXO — so' vale enquanto a agenda do AceleraX nao puder ser lida
    /// (o item "pedidos" da tela Integracao ERP › Agenda de sincronizacao manda no resto).</summary>
    public int PollSeconds { get; set; } = 300;
    /// <summary>De quanto em quanto tempo o worker confere a agenda do AceleraX.</summary>
    public int AgendaTickSeconds { get; set; } = 60;
    public string? Desde { get; set; }
    public int? Limite { get; set; }
    /// <summary>true = processa UMA rodada e encerra (one-shot p/ teste).
    /// false = loop a cada PollSeconds (modo servico continuo).</summary>
    public bool RunOnce { get; set; }
    /// <summary>Teste: busca e loga o payloadErp mas NAO carrega/chama a DLL nem marca
    /// integrado (o pedido segue pendente). Nada e' gravado no ERP.</summary>
    public bool SkipDll { get; set; }
    /// <summary>Teste: processa somente este codi_ped (mira 1 pedido).</summary>
    public string? Somente { get; set; }
    /// <summary>Situacao dos pedidos no ERP: so' confere os integrados ha' ate' N dias
    /// (pedido antigo ja' nao muda). null/0 = todos.</summary>
    public int? SituacaoDias { get; set; } = 180;
}

/// <summary>
/// Esteira de faturamento do HUB 2LN (Esteira/EsteiraWorker): transporte das Solicitacoes de Faturamento entre o
/// AceleraX e as tabelas de passagem APEX_EST_AX_* (criadas pelo HUB). A frequencia vem da agenda do AceleraX
/// (entidade "esteira"); sem agenda, vale o intervalo fixo.
/// </summary>
public sealed class EsteiraOptions
{
    /// <summary>false = worker desligado (cliente sem a esteira do HUB).</summary>
    public bool Ativo { get; set; } = true;
    /// <summary>Intervalo FIXO — so' vale enquanto a agenda do AceleraX nao puder ser lida ou nao tiver a entidade.</summary>
    public int IntervaloFixoSegundos { get; set; } = 300;
    /// <summary>Tentativas de envio de UMA fase recusada pelo AceleraX (4xx) antes de virar ERRO.</summary>
    public int MaxTentativas { get; set; } = 5;
}
