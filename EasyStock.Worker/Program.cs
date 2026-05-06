using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Worker;
using EasyStock.Worker.BackgroundServices;
using EasyStock.Worker.Collectors;
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

// Options
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

// Event collectors
builder.Services.AddScoped<IColetorEventoNotificacao, ColetorProdutosVencendo>();
builder.Services.AddScoped<IColetorEventoNotificacao, ColetorAssinaturasExpirando>();

// Background services
builder.Services.AddHostedService<OutboxListenService>();
builder.Services.AddHostedService<DispatcherOutboxService>();
builder.Services.AddHostedService<AvaliadorRotinasService>();
builder.Services.AddHostedService<ColetorEventosDeEstadoService>();

// Health checks
builder.Services.AddHealthChecks();

var host = builder.Build();
host.Run();
