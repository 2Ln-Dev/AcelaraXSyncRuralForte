using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoErp.Service.Acelera;
using PedidoErp.Service.Core;
using PedidoErp.Service.Core.Data;
using PedidoErp.Service.Dll;
using PedidoErp.Service.Pedidos;
using PedidoErp.Service.Tabela;
using Serilog;
using Serilog.Filters;
using TwoLN.Licensing;

// cp1252 (a DLL fala ANSI Brasil) precisa do provider de code pages em .NET Core.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = Host.CreateApplicationBuilder(args);

// --- Windows Service (dispensa NSSM) --------------------------------------
builder.Services.AddWindowsService(o => o.ServiceName = "AceleraXPedidoErp");

// --- Logs (Serilog -> console + arquivo rotativo diario) ------------------
var logCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "pedido-erp-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30);

// Windows Event Log: SOMENTE eventos importantes do servico (marcados com a propriedade
// "EvtServico"): iniciou/parou, licenca invalida, falha fatal. Os eventos NORMAIS de
// sincronizacao continuam so' no arquivo. O sink e' Windows-only.
// SO' quando rodando como Windows Service: em console/dev o EventLog exigiria admin p/
// criar a source e quebraria o app — la' os eventos ficam em console+arquivo. No servico
// (LocalSystem) a source e' criada e os eventos EvtServico vao para o Event Log.
#pragma warning disable CA1416 // EventLog e' Windows-only; protegido pelos guards abaixo.
if (OperatingSystem.IsWindows()
    && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    logCfg = logCfg.WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty("EvtServico"))
        .WriteTo.EventLog(source: "AceleraX Sync", logName: "Application", manageEventSource: true));
#pragma warning restore CA1416

Log.Logger = logCfg.CreateLogger();
builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// --- Configuracao (options) -----------------------------------------------
builder.Services.Configure<AceleraOptions>(builder.Configuration.GetSection("Acelera"));
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection("Oracle"));
builder.Services.Configure<DllOptions>(builder.Configuration.GetSection("Dll"));
builder.Services.Configure<PedidoOptions>(builder.Configuration.GetSection("Pedido"));

// --- Licenciamento (pacote 2LN.Licensing) ---------------------------------
#if LICENCA_OBRIGATORIA
// Release (producao): a config de licenca e' FIXA no binario, adicionada por ULTIMO =
// prioridade maxima -> sobrescreve appsettings, variaveis de ambiente e linha de comando.
// O cliente NAO desliga a licenca editando JSON. Em Debug a diretiva nao existe -> vale
// o appsettings (dev sem licenca). Mesma abordagem do HubPadrao. Ao derivar outro
// produto/cliente, troque o Produto abaixo.
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Licenca:Ativo"] = "true",
    ["Licenca:Produto"] = "acelera-x-sync",
    ["Licenca:SupabaseUrl"] = "https://zrztcylqqpiodampqojd.supabase.co",
    ["Licenca:SupabaseAnonKey"] = "sb_publishable_gfGco4p9hhQYUqTPixZKtw_EnNrrarm",
    ["Licenca:ToleranciaOfflineDias"] = "7",
});
#endif
// Registra LicenseState (singleton) + LicenseChecker (BackgroundService que revalida a
// cada IntervaloHoras). O worker consulta LicenseState.Current no gate de cada rodada
// (headless — sem o middleware HTTP UseAppLicensing).
builder.Services.AddAppLicensing(builder.Configuration);

// --- Servicos --------------------------------------------------------------
builder.Services.AddSingleton<IDbConnectionFactory, OracleConnectionFactory>();
builder.Services.AddHttpClient<AceleraClient>();
builder.Services.AddSingleton<ITabelaBarterService, TabelaBarterService>();
builder.Services.AddSingleton<DllBridge>();
builder.Services.AddHostedService<PedidoWorker>();

var host = builder.Build();

// Eventos do servico -> tambem no Windows Event Log (propriedade EvtServico).
var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
Log.ForContext("EvtServico", true).Information("Servico AceleraX Sync INICIADO (v{Versao}).", versao);
try
{
    host.Run();
    Log.ForContext("EvtServico", true).Information("Servico AceleraX Sync ENCERRADO.");
}
catch (Exception ex)
{
    // Falha estrutural/fatal na inicializacao ou execucao do host -> Event Log + rethrow
    // (deixa o SCM registrar a falha para o recovery reiniciar).
    Log.ForContext("EvtServico", true).Fatal(ex, "Falha FATAL na inicializacao/execucao do servico AceleraX Sync.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
