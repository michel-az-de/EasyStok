using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Worker;
using EasyStock.Worker.BackgroundServices;
using OpenTelemetry.Metrics;
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
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection nao configurada.");

// Sem JWT/HTTP — bypassa filter multi-tenant via SuperAdmin stub. SEM ISSO, queries do
// Worker que nao usam IgnoreQueryFilters() retornam 0 rows (SLA monitor, notificacoes).
builder.Services.AddScoped<EasyStock.Application.Ports.Output.ICurrentUserAccessor,
    WorkerCurrentUserAccessor>();

builder.Services
    .AddEasyStockPostgreInfrastructure(connStr, builder.Configuration)
    .AddEasyStockNotificationsRepositories();

// Email service (reusa Infra.Async, sem chamar AddEasyStockAsyncInfrastructure completo).
// TryParse pra nao crashar startup com env malformed (Smtp__Port=abc, Smtp__EnableSsl=xyz).
var smtpSection = builder.Configuration.GetSection("Smtp");
if (smtpSection.Exists())
{
    var smtpHost = smtpSection["Host"] ?? "localhost";
    var smtpPort = int.TryParse(smtpSection["Port"], out var portParsed) ? portParsed : 587;
    var smtpSsl = !bool.TryParse(smtpSection["EnableSsl"], out var sslParsed) || sslParsed;
    builder.Services.AddSingleton<IEmailService>(sp => new SmtpEmailService(
        smtpHost,
        smtpPort,
        smtpSection["Username"] ?? "",
        smtpSection["Password"] ?? "",
        smtpSection["FromEmail"] ?? "noreply@easystock.com",
        smtpSection["FromName"] ?? "EasyStock",
        smtpSsl));
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

// Outbox de eventos de integração externa (F4.c) — consome
// OutboxEventoIntegracao e despacha via handlers registrados.
// Pode ser desligado via Integration:Outbox:Enabled=false (default true).
builder.Services.AddHostedService<IntegrationOutboxBackgroundService>();

// Health checks removidos — Worker nao expoe HTTP. Background Worker do Render so
// monitora liveness do processo (sem hit em endpoint /health).

// OpenTelemetry: registra metric sources (Meters criados em SlaMonitorService /
// IntegrationEventDispatcher / NotificacoesDispatcherOrchestrator). Exporter OTLP
// soh eh adicionado se OTEL_EXPORTER_OTLP_ENDPOINT estiver setado — sem isso
// os counters ficam in-memory (sem warning de export falho).
// Runtime instrumentation (~15 series GC/threadpool/exceptions cada 10s) NAO incluido
// por enquanto pra conservar quota de "active series" em backends free-tier.
// Adicionar quando custo de telemetria for validado.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("EasyStock.Helpdesk")
            .AddMeter("EasyStock.Integration.Outbox")
            .AddMeter("EasyStock.Notifications");

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter();
        }
    });

var host = builder.Build();
host.Run();
