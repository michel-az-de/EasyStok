using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Storage;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Worker;
using EasyStock.Worker.BackgroundServices;
using EasyStock.Worker.DependencyInjection;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal.Mock.DependencyInjection;
using Serilog;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal("UNHANDLED EXCEPTION (IsTerminating={IsTerminating}): {Exception}",
        e.IsTerminating, e.ExceptionObject);
    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error("UNOBSERVED TASK EXCEPTION: {Exception}", e.Exception);
    e.SetObserved();
};

var builder = Host.CreateApplicationBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Services.AddSerilog();

// Options — WorkerOptions mantido por retro-compat (lê seção "Worker"); seção canônica é
// "Notifications:Hosting" lida via AddNotificationsCore.
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.Section));

// Infrastructure
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");

builder.Services
    .AddEasyStockPostgreInfrastructure(connStr, builder.Configuration)
    .AddEasyStockNotificationsRepositories();

// Email service (reusa Infra.Async, sem chamar AddEasyStockAsyncInfrastructure completo)
var smtpSection = builder.Configuration.GetSection("Smtp");
if (smtpSection.Exists())
{
    builder.Services.AddSingleton<IEmailService>(sp => new SmtpEmailService(
        smtpSection["Host"] ?? "localhost",
        int.Parse(smtpSection["Port"] ?? "587"),
        smtpSection["Username"] ?? "",
        smtpSection["Password"] ?? "",
        smtpSection["FromEmail"] ?? "noreply@easystock.com",
        smtpSection["FromName"] ?? "EasyStock",
        bool.Parse(smtpSection["EnableSsl"] ?? "true")));
}
else
{
    builder.Services.AddSingleton<IEmailService, ConsoleEmailService>();
}

// Notifications infra (canal adapters + Scriban renderer)
builder.Services.AddNotificationsInfra(builder.Configuration);

// Application services + use cases (NotificadorService, RotinaScheduler, ResolvedorCanal, etc.)
builder.Services.AddEasyStockApplication();

// Advisory lock utility
builder.Services.AddScoped<PostgresAdvisoryLock>();

// Hosting do pipeline de notificações — coletores e dispatcher orchestrator já vêm de
// AddEasyStockNotificationsRepositories() acima. AddNotificationsHosting chama
// AddNotificationsCore internamente (idempotente), então não duplicar.
// Mode lido de "Notifications:Hosting:Mode".
builder.Services
    .AddNotificationsHosting(builder.Configuration)
    .AddPostgresOutboxSignaler(builder.Configuration);

// Jobs de Helpdesk (notificação tem manutenção própria via AddPostgresOutboxSignaler).
// SlaMonitorService monitora tickets e gera EventoNotificacao — pertence ao Worker mesmo.
builder.Services.AddHostedService<SlaMonitorService>();

// Lembretes de pedidos agendados (mobile_orders.scheduled_delivery_at):
// no dia, 1h antes, 10min antes. Idempotencia via colunas agendamento_notificado_*_em.
builder.Services.AddHostedService<AgendamentoNotificacaoService>();

// Monitor de saude de endpoints publicos. Abre ticket via /api/ci/tickets
// quando >threshold falhas consecutivas. Idempotencia via tabela
// endpoint_health_state + cooldown 24h.
builder.Services.AddHttpClient("endpoint-health");
builder.Services.AddHostedService<EndpointHealthMonitorService>();

// Outbox de eventos de integração externa (F4.c) — consome
// OutboxEventoIntegracao e despacha via handlers registrados.
// Pode ser desligado via Integration:Outbox:Enabled=false (default true).
builder.Services.AddHostedService<IntegrationOutboxBackgroundService>();

// Storage de arquivos (IFileStorage): o motor de relatórios escreve/lê artefatos via
// IFileStorage. As implementações vivem em Infra.Async.Storage (compartilhadas com a API).
// Sem este registro o Worker não resolvia IFileStorage e o ReportRunner falhava em runtime
// (gap fechado — provider lido de "FileStorage:Provider", default Local).
builder.Services.AddEasyStockFileStorageCore(builder.Configuration);

// Motor de relatórios assíncrono (PR-C0 — ADR-R02/R03/R04/R06/R07)
// Registra ReportRunnerBackgroundService + ReportWatchdogBackgroundService +
// WorkerCurrentUserAccessor (override ADR-R06) + ReportExecutionContext (AsyncLocal).
builder.Services.AddReportingWorker();

// IMemoryCache: o ConfigFiscalResolver (registrado em AddEasyStockPostgreInfrastructure)
// faz cache de 60s da config fiscal por tenant. Sem este registro o
// ReprocessarContingenciaBackgroundService quebrava em runtime ao resolver
// ReprocessarContingenciaUseCase → IConfigFiscalResolver → IMemoryCache (só a API
// registrava o cache, via AddEasyStockCache). Singleton consumido por Scoped: OK.
builder.Services.AddMemoryCache();

// Modulo Fiscal NFC-e (F4) — Polly pipelines + adapters Focus NFe + Mock + jobs background
builder.Services.AddEasyStockIntegrationResilience();
builder.Services.AddFocusNFeAdapter(builder.Configuration);
builder.Services.AddMockFiscalGateway();
// Scoped (não Singleton): a factory consome IGatewayFiscal Scoped — como Singleton
// capturava gateways scoped (captive dependency / lifetime mismatch). Mesmo fix da
// API (#193). Os jobs (hosted services) já resolvem os use cases fiscais via
// IServiceScope (ex.: ReprocessarContingenciaBackgroundService.CreateScope()), então
// a factory scoped resolve corretamente dentro do scope.
builder.Services.AddScoped<EasyStock.Application.Ports.Output.Fiscal.IGatewayFiscalFactory,
    EasyStock.Infra.Integrations.Fiscal.GatewayFiscalFactory>();
builder.Services.AddDataProtection();
builder.Services.AddHostedService<ReprocessarContingenciaBackgroundService>();
builder.Services.AddHostedService<RenovacaoCertificadoA1BackgroundService>();

// Health checks
builder.Services.AddHealthChecks();

// Validação de DI sob demanda (CI/diagnóstico): `dotnet run -- --validate-di` constrói
// o grafo com ValidateOnBuild + ValidateScopes, pegando captive dependencies
// (singleton↔scoped) e serviços não-resolvíveis. Foi assim que o lifetime mismatch da
// factory fiscal (#194) passou silencioso no Generic Host (não valida por padrão).
//
// Esta ferramenta encontrou e fechamos 2 crashes latentes que dariam "erro genérico":
//   • IFileStorage  — motor de relatórios (ReportRunner/Watchdog). Realocado p/ Infra.Async.
//   • IMemoryCache  — cadeia fiscal (ReprocessarContingenciaUseCase → IConfigFiscalResolver).
//
// Mantido CONDICIONAL (não roda no startup normal): o grafo ainda não é 100% resolvível
// porque o Worker reusa AddEasyStockApplication() — que registra TODOS os use cases da
// API (auth, PIX, uploads, faturas, admin, inteligência, tickets). Esses use cases nunca
// são resolvidos pelos jobs do Worker (registros mortos), mas o ValidateOnBuild os reporta.
// Um gate incondicional verde exigiria fatiar AddEasyStockApplication para o Worker
// registrar só o que executa — refatoração de registro compartilhado (follow-up).
if (args.Contains("--validate-di"))
{
    using var validationProvider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });
    Log.Information("Worker: grafo de DI validado com sucesso.");
    Log.CloseAndFlush();
    return;
}

var host = builder.Build();
host.Run();
