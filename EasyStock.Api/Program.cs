using EasyStock.Api.Configuration;
using EasyStock.Api.DependencyInjection;
using EasyStock.Api.Hosting;
using EasyStock.Api.Startup;
using EasyStock.Application.DependencyInjection;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Async.DependencyInjection;
using Serilog;

// Handler global para exceções não tratadas que derrubam o processo
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal("UNHANDLED EXCEPTION (processo encerrando={IsTerminating}): {Exception}",
        e.IsTerminating, e.ExceptionObject);
    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error("UNOBSERVED TASK EXCEPTION: {Exception}", e.Exception);
    e.SetObserved(); // evita crash do processo por Task não observada
};

var builder = WebApplication.CreateBuilder(args);

// Garantir que diretório de logs existe antes do Serilog iniciar
{
    var logDir = builder.Configuration["LogSettings:LogDirectory"] is { Length: > 0 } dir
        ? dir
        : Path.Combine(AppContext.BaseDirectory, "logs");
    try { Directory.CreateDirectory(logDir); } catch { /* best-effort */ }
}

// LevelSwitch permite controlar o nível mínimo em tempo real via DiagnosticoModeService.
// Criado aqui (antes do Serilog) para ser injetado no logger e nos serviços.
var diagLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

// Configure Serilog — máximo de informações em cada log entry
var loggerConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.ControlledBy(diagLevelSwitch)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .Enrich.WithMachineName();

// Logs via OTLP quando ha collector configurado (issue 1002; na VM consolidada,
// OpenTelemetry__OtlpEndpoint -> otel-collector). O sink inclui trace_id/span_id do
// Activity corrente => logs correlacionados com os traces no OpenObserve. Opt-in:
// sem a config (dev local/CI), nenhum sink extra.
if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 } otlpLogsEndpoint)
{
    loggerConfiguration = loggerConfiguration.WriteTo.OpenTelemetry(o =>
    {
        o.Endpoint = otlpLogsEndpoint;
        o.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
        o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = "EasyStock.Api" };
    });
}

Log.Logger = loggerConfiguration.CreateLogger();

builder.Host.UseSerilog();

// ── Core MVC ─────────────────────────────────────────────────────────────────
builder.Services.AddEasyStockCoreMvc();

// ── Feature DI groups ─────────────────────────────────────────────────────────
builder.Services.AddEasyStockSwagger();
builder.Services.AddEasyStockAuth(builder.Configuration);
builder.Services.AddEasyStockCors(builder.Configuration, builder.Environment);
builder.Services.AddEasyStockRateLimit();
builder.Services.AddEasyStockObservability(builder.Configuration, builder.Environment);
builder.Services.AddEasyStockCache(builder.Configuration);
builder.Services.AddEasyStockFileStorage(builder.Configuration);

// ── Database ──────────────────────────────────────────────────────────────────
var databaseProvider = builder.Configuration[ConfigurationKeys.DatabaseProvider] ?? "Auto";
var postgresConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionDefault);
var (resolvedProvider, infraState) = await DatabaseModule.ConfigureAsync(
    builder, databaseProvider, postgresConnectionString);

// ── Application + Async Infra ─────────────────────────────────────────────────
builder.Services.AddEasyStockApplication();
builder.Services.AddEasyStockStorefrontUseCases();
// #449: registra IMercadoPagoClient (StubMercadoPagoClient em Development via
// MercadoPago:UseStub; cliente real em producao). Sem isto, IniciarCheckoutUseCase
// nao resolve e o boot da API falha em Development (ValidateOnBuild).
builder.Services.AddMercadoPagoClient(builder.Configuration);
builder.Services.AddReportingApi();
builder.Services.Configure<EasyStock.Application.Services.PedidoEstoqueOptions>(
    builder.Configuration.GetSection("Pedidos"));
builder.Services.AddEasyStockAsyncInfrastructure(builder.Configuration);

// ── Storefront — WhatsApp OTP provider (stub em Development, real em Prod) ────
// TASK-EZ-AUTH-001: stub em ambientes nao-Production por default. Provider real
// (Meta WhatsApp Cloud API) entra em TASK-EZ-WA-001 apos Meta Business
// Verification (TASK-HUM-001). Flag explicita Otp:UseStub destrava o stub em
// Production enquanto a verificacao do Meta nao sai (issue #677 — Casa da Baba):
// codigo so vai pro docker logs, nao volta no response. Em Production sem flag
// e sem provider real, AuthController nao resolve — fail fast intencional.
var otpUseStub = builder.Configuration.GetValue<bool>("Otp:UseStub");
if (otpUseStub || !builder.Environment.IsProduction())
{
    builder.Services.AddEasyStockWhatsAppStub();
}

// ── Storefront — CEP lookup (ViaCEP em prod, NoOp por default) ────────────────
// TASK-EZ-FRETE-001: feature flag ENABLE_VIACEP_LOOKUP (default false). Quando
// off, registra NoOpCepLookupClient (não bate na API externa). Quando on,
// registra ViaCepLookupClient com timeout 1s.
builder.Services.AddEasyStockCepLookup(builder.Configuration);

// Storefront — geocoding p/ frete por raio (ADR-0017). Flag ENABLE_NOMINATIM_GEOCODING
// (default false → NoOp; frete cai pra zona). Liga quando o serviço/self-host subir.
builder.Services.AddEasyStockGeocoding(builder.Configuration);

builder.Services.Configure<EasyStockConfiguracoes>(
    builder.Configuration.GetSection(ConfigurationKeys.SectionEasyStock));

// Registrar IEasyStockConfiguracoes para injeção em use cases
builder.Services.AddScoped<EasyStock.Application.Configuration.IEasyStockConfiguracoes>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EasyStockConfiguracoes>>().Value);

// ── Notifications: infra (canais + Scriban) + hosting (orchestrators) + signaler ──
builder.Services.AddEasyStockNotificationsModule(builder.Configuration);

// ── Background + Mobile + Misc ────────────────────────────────────────────────
builder.Services.AddEasyStockApiServices(builder.Configuration);

// DiagnosticoModeService: Singleton que controla LoggingLevelSwitch em tempo real.
builder.Services.AddSingleton(diagLevelSwitch);
builder.Services.AddSingleton<EasyStock.Api.Observability.DiagnosticoModeService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Modo migrate-only (release_command do deploy) ─────────────────────────────
// Aplica migrations pendentes e ENCERRA, sem subir o servidor. O deploy (Fly
// release_command) roda este modo numa máquina temporária ANTES de promover a
// versão nova; se a migration falhar (exit != 0), o deploy é abortado e a versão
// antiga continua servindo — produção nunca fica com código novo + banco velho.
if (args.Contains("--migrate-only"))
{
    using var migrateScope = app.Services.CreateScope();
    var migrateDb = migrateScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
    using var _ = migrateDb.UseRowLevelSecurityBypass();
    var pendentes = (await migrateDb.Database.GetPendingMigrationsAsync()).ToList();
    app.Logger.LogInformation("[migrate-only] {Count} migration(s) pendente(s): {Lista}",
        pendentes.Count, pendentes.Count == 0 ? "(nenhuma)" : string.Join(", ", pendentes));
    await migrateDb.Database.MigrateAsync();
    app.Logger.LogInformation("[migrate-only] Concluido. Encerrando sem subir o servidor.");
    return;
}

// ForwardedHeaders: Fly/Render/etc fazem TLS no edge e mandam HTTP com
// X-Forwarded-Proto=https. Sem isso o UseHttpsRedirection estoura 400.
//
// CRITICO (incidente login-admin / #277 / #657): com KnownNetworks/KnownProxies VAZIOS
// o middleware NAO confia em nenhum proxy e IGNORA o X-Forwarded-For -> RemoteIpAddress
// fica sendo o IP do container que abriu a conexao (o Caddy, ou o BFF Web/Admin que chama
// a API server-side). Resultado: o rate limiter de auth (particionado por IP) e a auditoria
// de IP colapsam numa unica particao por-container -> 10/min compartilhado por TODOS os
// admins -> login some em 429. A API so e alcancavel pela rede interna do Docker (nada
// chega direto), entao confiar nas faixas privadas e seguro e restaura o IP REAL do cliente.
// ForwardLimit=1: consome apenas a entrada mais a direita do X-Forwarded-For (a que o proxy
// confiavel anexou) — um X-Forwarded-For forjado pelo cliente fica a esquerda e nao e usado.
var forwardedHeaders = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    ForwardLimit = 1
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
foreach (var rede in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8" })
    forwardedHeaders.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(rede));
app.UseForwardedHeaders(forwardedHeaders);

// ── Migrations + Seed (PostgreSQL only) ───────────────────────────────────────
await StartupMigrationsAndSeed.RunAsync(app, infraState, databaseProvider, resolvedProvider);

// Restaura modo de logging verbose salvo no DB (após migrations estarem aplicadas).
if (resolvedProvider is "postgresql")
{
    try
    {
        var diagMode = app.Services.GetRequiredService<EasyStock.Api.Observability.DiagnosticoModeService>();
        await diagMode.RestoreFromDbAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "[DiagnosticoMode] Falha ao restaurar modo — usando Information.");
    }
}

// ── Startup hardening: JWT, connection strings, Mobile API key ────────────────
StartupHardening.Validate(builder, postgresConnectionString);
// Fuso de Brasilia: em producao recusa subir se degradou (ex.: imagem sem tzdata).
StartupHardening.ValidateTimezone(builder.Environment);

Log.Information("""

    ======================================
      EasyStock API
      Ambiente:     {Environment}
      Banco:        {Provider} (configurado: {Configured})
      Raiz (/):     → redireciona para /swagger
      Swagger:      /swagger
      Diagnostico:  /diagnostico
      Health:       /health, /health/live, /health/ready
    ======================================
    """,
    app.Environment.EnvironmentName, resolvedProvider, databaseProvider);

// === Middleware pipeline (transcricao verbatim em Hosting/PipelineExtensions.cs) ===
app.UseEasyStockPipeline();

// Modo openapi-export: o Swashbuckle.AspNetCore.Cli (scripts/export-openapi.ps1)
// captura o IServiceProvider via HostFactoryResolver e nao precisa do Host
// iniciado. Saimos antes de app.Run() pra evitar Host.StartAsync() — que
// dispararia hosted services do pipeline de notificacoes dependendo de DI
// Postgres-only (DispatcherLoopHostedService precisa de INotificacoesDispatcherOrchestrator).
if (string.Equals(Environment.GetEnvironmentVariable("OPENAPI_EXPORT"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("[openapi-export] OPENAPI_EXPORT=true — saindo antes de app.Run().");
    return;
}

app.Run();

public partial class Program { }
