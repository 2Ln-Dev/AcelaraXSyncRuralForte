using BaseSync.Service.Acelera;
using BaseSync.Service.Core;
using BaseSync.Service.Core.Data;
using BaseSync.Service.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Filters;
using TwoLN.Licensing;

var builder = Host.CreateApplicationBuilder(args);

// --- Windows Service (dispensa NSSM) --------------------------------------
builder.Services.AddWindowsService(o => o.ServiceName = "AceleraXBaseSync");

// --- Logs (Serilog -> console + arquivo rotativo diario) ------------------
var logCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "base-sync-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30);

// Windows Event Log: SOMENTE eventos importantes do servico (marcados com "EvtServico"):
// iniciou/parou, licenca invalida, falha fatal. Os eventos NORMAIS de sincronizacao ficam
// so' no arquivo. SO' quando rodando como Windows Service (em console/dev criar a source
// exigiria admin). Mesmo padrao do PedidoErp.Service.
#pragma warning disable CA1416 // EventLog e' Windows-only; protegido pelos guards abaixo.
if (OperatingSystem.IsWindows()
    && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    logCfg = logCfg.WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty("EvtServico"))
        .WriteTo.EventLog(source: "AceleraX Base Sync", logName: "Application", manageEventSource: true));
#pragma warning restore CA1416

Log.Logger = logCfg.CreateLogger();
builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// --- Configuracao (options) -----------------------------------------------
builder.Services.Configure<AceleraOptions>(builder.Configuration.GetSection("Acelera"));
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection("Oracle"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));

// --- Licenciamento (pacote 2LN.Licensing) ---------------------------------
#if LICENCA_OBRIGATORIA
// Release (producao): a config de licenca e' FIXA no binario, adicionada por ULTIMO =
// prioridade maxima -> sobrescreve appsettings, variaveis de ambiente e linha de comando.
// Em Debug a diretiva nao existe -> vale o appsettings (dev sem licenca). Mesma abordagem
// do HubPadrao/PedidoErp.Service.
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Licenca:Ativo"] = "true",
    ["Licenca:Produto"] = "acelera-x-sync",
    ["Licenca:SupabaseUrl"] = "https://zrztcylqqpiodampqojd.supabase.co",
    ["Licenca:SupabaseAnonKey"] = "sb_publishable_gfGco4p9hhQYUqTPixZKtw_EnNrrarm",
    ["Licenca:ToleranciaOfflineDias"] = "7",
});
#endif
builder.Services.AddAppLicensing(builder.Configuration);

// --- Servicos --------------------------------------------------------------
builder.Services.AddSingleton<IDbConnectionFactory, OracleConnectionFactory>();
builder.Services.AddSingleton<OracleReader>();
builder.Services.AddHttpClient<BaseApiClient>();
builder.Services.AddHostedService<BaseSyncWorker>();

var host = builder.Build();

var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
Log.ForContext("EvtServico", true).Information("Servico AceleraX Base Sync INICIADO (v{Versao}).", versao);
try
{
    host.Run();
    Log.ForContext("EvtServico", true).Information("Servico AceleraX Base Sync ENCERRADO.");
}
catch (Exception ex)
{
    Log.ForContext("EvtServico", true).Fatal(ex, "Falha FATAL na inicializacao/execucao do servico AceleraX Base Sync.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
