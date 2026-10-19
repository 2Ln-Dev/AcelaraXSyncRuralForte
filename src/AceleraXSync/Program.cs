using System.Text;
using AceleraXSync.Acelera;
using AceleraXSync.Core;
using AceleraXSync.Core.Data;
using AceleraXSync.Dll;
using AceleraXSync.Pedidos;
using AceleraXSync.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Filters;
using TwoLN.Licensing;

// cp1252 (a DLL AGB fala ANSI Brasil) precisa do provider de code pages em .NET Core.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Segredos em claro do appsettings.Production.json (senha Oracle, URL e chave da API)
// viram enc:v1:... ANTES de a configuracao ser lida — o arquivo nunca fica em claro.
AceleraXSync.Core.Security.SecretProtector.SelarSegredos(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);

// --- Windows Service (um unico servico p/ os dois workers) ----------------
// Nome HubAceleraX (nao "HubPadrao"/"Hub2LN": ja' existe esse servico de outro produto).
builder.Services.AddWindowsService(o => o.ServiceName = "HubAceleraX");

// --- Logs (Serilog -> console + arquivo rotativo diario) ------------------
var logCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "hubacelerax-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30);

// Windows Event Log: SOMENTE eventos importantes do servico (marcados com "EvtServico"):
// iniciou/parou, licenca invalida, falha fatal. SO' quando rodando como Windows Service.
#pragma warning disable CA1416 // EventLog e' Windows-only; protegido pelos guards abaixo.
if (OperatingSystem.IsWindows()
    && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    logCfg = logCfg.WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty("EvtServico"))
        .WriteTo.EventLog(source: "Hub AceleraX", logName: "Application", manageEventSource: true));
#pragma warning restore CA1416

Log.Logger = logCfg.CreateLogger();
builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// --- Configuracao (options) -----------------------------------------------
builder.Services.Configure<AceleraOptions>(builder.Configuration.GetSection("Acelera"));
builder.Services.PostConfigure<AceleraOptions>(o =>
{
    // URL e chave cifradas (ApiUrlEnc/ApiKeyEnc) valem sobre as em claro.
    o.ApiUrl = AceleraXSync.Core.Security.SecretProtector.Resolver(o.ApiUrl, o.ApiUrlEnc);
    o.ApiKey = AceleraXSync.Core.Security.SecretProtector.Resolver(o.ApiKey, o.ApiKeyEnc);
});
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection("Oracle"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.Configure<DllOptions>(builder.Configuration.GetSection("Dll"));
builder.Services.Configure<PedidoOptions>(builder.Configuration.GetSection("Pedido"));

// --- Licenciamento (pacote 2LN.Licensing) ---------------------------------
#if LICENCA_OBRIGATORIA
// Release (producao): a config de licenca e' FIXA no binario, adicionada por ULTIMO =
// prioridade maxima -> sobrescreve appsettings/env/linha de comando. Em Debug a diretiva
// nao existe -> vale o appsettings (dev sem licenca). Ao derivar outro produto/cliente,
// troque o Produto abaixo.
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Licenca:Ativo"] = "true",
    ["Licenca:Produto"] = "acelera-x-sync",
    ["Licenca:SupabaseUrl"] = "https://zrztcylqqpiodampqojd.supabase.co",
    ["Licenca:SupabaseAnonKey"] = "sb_publishable_gfGco4p9hhQYUqTPixZKtw_EnNrrarm",
    ["Licenca:ToleranciaOfflineDias"] = "7",
});
#endif
// LicenseState (singleton) + revalidacao periodica. Os dois workers consultam
// LicenseState.Current no gate de cada rodada (headless, sem middleware HTTP).
builder.Services.AddAppLicensing(builder.Configuration);

// --- Servicos (infra comum + os dois fluxos) ------------------------------
builder.Services.AddSingleton<IDbConnectionFactory, OracleConnectionFactory>();

// Carga de base (ERP -> AceleraX)
builder.Services.AddSingleton<OracleReader>();
builder.Services.AddHttpClient<BaseApiClient>();
builder.Services.AddHostedService<BaseSyncWorker>();

// Pedidos (AceleraX -> ERP, via DLL AGB)
builder.Services.AddHttpClient<AceleraClient>();
// Agenda por entidade (tela Integracao ERP do AceleraX), usada pelos DOIS workers.
builder.Services.AddSingleton<AgendaService>();
builder.Services.AddSingleton<DllBridge>();
builder.Services.AddSingleton<OrigemPedidoErp>();
builder.Services.AddSingleton<UsuarioPedidoErp>();
builder.Services.AddHostedService<PedidoWorker>();

var host = builder.Build();

var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
Log.ForContext("EvtServico", true).Information("Servico Hub AceleraX INICIADO (v{Versao}).", versao);
try
{
    host.Run();
    Log.ForContext("EvtServico", true).Information("Servico Hub AceleraX ENCERRADO.");
}
catch (Exception ex)
{
    Log.ForContext("EvtServico", true).Fatal(ex, "Falha FATAL na inicializacao/execucao do Servico Hub AceleraX.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
