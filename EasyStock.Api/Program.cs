using EasyStock.Api.Configuration;
using EasyStock.Api.DependencyInjection;
using EasyStock.Api.Hosting;
using EasyStock.Api.Startup;
using EasyStock.Application.DependencyInjection;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Storage;
using Serilog;
using System.Reflection;

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
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.ControlledBy(diagLevelSwitch)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .Enrich.WithMachineName()
    .CreateLogger();

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
var mongoConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionMongo);
var (resolvedProvider, infraState) = await DatabaseModule.ConfigureAsync(
    builder, databaseProvider, postgresConnectionString, mongoConnectionString);

// ── Application + Async Infra ─────────────────────────────────────────────────
builder.Services.AddEasyStockApplication();
builder.Services.AddEasyStockStorefrontUseCases();
builder.Services.AddReportingApi();
builder.Services.Configure<EasyStock.Application.Services.PedidoEstoqueOptions>(
    builder.Configuration.GetSection("Pedidos"));
builder.Services.AddEasyStockAsyncInfrastructure(builder.Configuration);

// ── Storefront — WhatsApp OTP provider (stub em Development, real em Prod) ────
// TASK-EZ-AUTH-001: stub apenas em ambientes nao-Production. Provider real
// (Meta WhatsApp Cloud API) entra em TASK-EZ-WA-001 apos Meta Business
// Verification (TASK-HUM-001). Em Production sem provider real, AuthController
// nao resolve — fail fast intencional.
if (!builder.Environment.IsProduction())
{
    builder.Services.AddEasyStockWhatsAppStub();
}

// ── Storefront — CEP lookup (ViaCEP em prod, NoOp por default) ────────────────
// TASK-EZ-FRETE-001: feature flag ENABLE_VIACEP_LOOKUP (default false). Quando
// off, registra NoOpCepLookupClient (não bate na API externa). Quando on,
// registra ViaCepLookupClient com timeout 1s.
builder.Services.AddEasyStockCepLookup(builder.Configuration);

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
app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

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
StartupHardening.Validate(builder, postgresConnectionString, mongoConnectionString);

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

// ── Middleware pipeline ───────────────────────────────────────────────────────
// ExceptionHandler deve ser o primeiro middleware para capturar exceções de qualquer
// middleware abaixo, incluindo swagger, static files e autenticação.
app.UseExceptionHandler();

// ResponseCompression precisa rodar cedo, antes de StaticFiles e do request logging,
// pra ter chance de capturar o output dos middlewares seguintes.
app.UseResponseCompression();
app.UseMiddleware<EasyStock.Api.Middleware.SecurityHeadersMiddleware>();

// Correlation ID propagation
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(correlationId))
        correlationId = Guid.NewGuid().ToString();

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

// Serilog Request Logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms ({ResponseSize} bytes)";

    options.GetLevel = (ctx, _, ex) =>
    {
        if (ex is not null || ctx.Response.StatusCode >= 500)
            return Serilog.Events.LogEventLevel.Error;

        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/diagnostico/ping", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/diagnostico/logs/live", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/diagnostico/historico", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
            return Serilog.Events.LogEventLevel.Debug;

        return Serilog.Events.LogEventLevel.Information;
    };

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        diagnosticContext.Set("ResponseSize", httpContext.Response.ContentLength ?? 0);

        var reqPath = httpContext.Request.Path.Value ?? "";
        var trafficType = reqPath switch
        {
            _ when reqPath.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                || reqPath.Contains("/ping", StringComparison.OrdinalIgnoreCase) => "infra",
            _ when reqPath.Contains("/diagnostico", StringComparison.OrdinalIgnoreCase)
                || reqPath.Contains("/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) => "polling",
            _ when reqPath.Contains("/logs/live", StringComparison.OrdinalIgnoreCase) => "sse",
            _ => "business"
        };
        diagnosticContext.Set("TrafficType", trafficType);

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User?.FindFirst("sub")?.Value;
        if (userId is not null)
            diagnosticContext.Set("UserId", userId);
        var empresaId = httpContext.User?.FindFirst("empresa_id")?.Value
                     ?? httpContext.User?.FindFirst("EmpresaId")?.Value;
        if (empresaId is not null)
            diagnosticContext.Set("EmpresaId", empresaId);
    };
});

// Swagger JSON cache (in-memory, TTL 1h — evita ~1900ms por request)
{
    var swaggerCache = new System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Body, string ContentType, DateTimeOffset CachedAt)>();
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "";
        var isSwaggerJson = path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase)
                         && path.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase)
                         && context.Request.Method == "GET";

        if (!isSwaggerJson) { await next(); return; }

        if (swaggerCache.TryGetValue(path, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < TimeSpan.FromHours(1))
        {
            context.Response.ContentType = cached.ContentType;
            context.Response.Headers["X-Swagger-Cache"] = "HIT";
            await context.Response.Body.WriteAsync(cached.Body);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new System.IO.MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = originalBody;
        }
        buffer.Position = 0;
        var body = buffer.ToArray();
        var contentType = context.Response.ContentType ?? "application/json";
        swaggerCache[path] = (body, contentType, DateTimeOffset.UtcNow);
        context.Response.Headers["X-Swagger-Cache"] = "MISS";
        await originalBody.WriteAsync(body);
    });
}

// Swagger UI (Development + Staging, ou flag Swagger:EnableInProduction)
var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || builder.Configuration.GetValue<bool>("Swagger:EnableInProduction");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EasyStock API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle        = "EasyStock API Docs";
        c.DefaultModelsExpandDepth(1);
        c.DefaultModelExpandDepth(3);
        c.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
        c.DisplayRequestDuration();
        c.EnableDeepLinking();
        c.EnableFilter();
        c.EnablePersistAuthorization();
        c.EnableTryItOutByDefault();
        c.ShowExtensions();
        c.ShowCommonExtensions();
        c.InjectStylesheet("/swagger-ui/custom.css");
        c.InjectJavascript("/swagger-ui/custom.js");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve uploaded files from local storage path (skip for S3 — served directly)
var fileStorageOptions = builder.Configuration
    .GetSection(ConfigurationKeys.SectionFileStorage)
    .Get<FileStorageOptions>() ?? new();
if (!string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    var localStorage = app.Services.GetRequiredService<EasyStock.Application.Ports.Output.Storage.IFileStorage>()
        as LocalFileStorage;
    if (localStorage is not null)
    {
        var rootPath = localStorage.GetRootPath();
        Directory.CreateDirectory(rootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(rootPath),
            RequestPath = fileStorageOptions.PublicBaseUrl
        });
    }
}

// Casa da Baba Mobile PWA — static files em /pwa/ com headers de service worker.
EasyStock.Api.Mobile.MobileModule.UseMobilePwa(app);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Sliding window: atualiza UltimoUsoEm da ClienteSession após cada request autenticado (ADR-0012).
app.UseMiddleware<EasyStock.Api.Middleware.ClienteSessionMiddleware>();
app.UseMiddleware<EasyStock.Api.Middleware.SubscriptionGateMiddleware>();
// Idempotencia: aplicado APOS auth para que ICurrentUserAccessor.EmpresaId esteja disponivel.
// Whitelist de POSTs criticos (R5: dedup retry de mobile/web).
EasyStock.Api.Middleware.IdempotencyMiddlewareExtensions.UseIdempotency(app, opts => opts
    .Add("/api/itensestoque")
    .Add("/api/itensestoque/estorno")
    .Add("/api/vendas")
    .Add("/api/mobile/vendas")
    .Add("/api/movimentacoes")
    .Add("/api/itensestoque/repor")
    .Add("/api/mobile/calculadora/criar-compra"));
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger", permanent: false))
   .ExcludeFromDescription();

// /console e /api-docs apontam pro EasyStock Console (UI dark sci-fi alternativa ao /swagger).
app.MapGet("/console", () => Results.Redirect("/api-docs/", permanent: false))
   .ExcludeFromDescription();
app.MapGet("/api-docs", () => Results.Redirect("/api-docs/index.html", permanent: false))
   .ExcludeFromDescription();
app.MapGet("/api-docs/", () => Results.Redirect("/api-docs/index.html", permanent: false))
   .ExcludeFromDescription();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

// /health/api: dependencias HTTP da API (PG + Redis + config) — NAO inclui dispatcher.
// Loop de notificacoes preso nao deve marcar a API inteira como down nos LBs.
app.MapHealthChecks("/health/api", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("api"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

// /health/dispatcher: heartbeats dos 3 BackgroundServices do pipeline de notificacoes.
// Healthy quando Mode=Disabled (pipeline em Worker separado). Unhealthy quando algum
// loop nao bate dentro de 5x intervalo configurado — sinal de pendurada.
app.MapHealthChecks("/health/dispatcher", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("dispatcher"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

// /health/version — endpoint do schema gate do PWA.
// Retorna a versao do contrato Mobile e SHA do build para que o cliente
// decida se pode aplicar update OTA (sync.js > maybeApplyPwaUpdate gate).
// Mantido separado do /api/mobile/version (que carrega features e OTA info)
// para que probes leves de health/version nao precisem subir a stack toda.
app.MapGet("/health/version", () =>
{
    var asm = Assembly.GetExecutingAssembly();
    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "unknown";
    return Results.Ok(new
    {
        apiVersion = info,
        mobileSchemaVersion = 2,
        buildSha = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "master",
        serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });
})
.AllowAnonymous()
.ExcludeFromDescription();

// Aviso de seguranca em Production: o default agora e RequireApiKey=true
// (appsettings.Production.json). Se algum operador setou Mobile__RequireApiKey=false
// via env var explicita, o sync mobile aceita requests anonimos e isso fica
// gritando como warning ate ele virar de volta.
if (app.Environment.IsProduction()
    && !app.Configuration.GetValue<bool>("Mobile:RequireApiKey"))
{
    app.Logger.LogWarning(
        "Mobile:RequireApiKey=false em Production por override explicito — " +
        "/api/mobile/sync aceita request anonimo. Restaurar default true assim " +
        "que todos os APKs estiverem pareados via /dispositivos.");
}

// Fail-fast Efi: so exige WebhookSecret quando o modulo Efi esta EFETIVAMENTE
// configurado (ClientId ou ClientSecret presentes). Sem credenciais Efi, o
// webhook /api/webhooks/pix nem processa nada util — exigir secret bloquearia
// ambientes que nao usam PIX (ex: Render de teste, dev). Quando Efi for
// configurado e Sandbox=false, ai sim aborta sem secret.
if (app.Environment.IsProduction())
{
    var efiClientId = app.Configuration["Efi:ClientId"];
    var efiClientSecret = app.Configuration["Efi:ClientSecret"];
    var efiConfigurado = !string.IsNullOrWhiteSpace(efiClientId)
                        || !string.IsNullOrWhiteSpace(efiClientSecret);

    var efiSecret = app.Configuration["Efi:WebhookSecret"];
    var efiAllowUnsigned = app.Configuration.GetValue<bool>("Efi:WebhookAllowUnsigned", false);
    var efiSandbox = app.Configuration.GetValue<bool>("Efi:Sandbox", true);

    if (!efiConfigurado)
    {
        app.Logger.LogInformation(
            "[Efi] Modulo nao configurado (ClientId/ClientSecret vazios) — webhook PIX " +
            "fica inerte. Setar Efi__ClientId/ClientSecret/WebhookSecret quando ativar PIX.");
    }
    else if (string.IsNullOrWhiteSpace(efiSecret) && !efiAllowUnsigned)
    {
        if (efiSandbox)
        {
            app.Logger.LogWarning(
                "[Efi] WebhookSecret vazio em Sandbox — /api/webhooks/pix vai aceitar requests " +
                "sem HMAC. OK em ambiente de teste; configure Efi__WebhookSecret antes de virar Sandbox=false.");
        }
        else
        {
            throw new InvalidOperationException(
                "Efi:WebhookSecret vazio em Production com Sandbox=false e Efi:WebhookAllowUnsigned=false. " +
                "Configurar Efi__WebhookSecret antes de receber PIX real ou setar explicitamente " +
                "Efi__WebhookAllowUnsigned=true (NAO recomendado).");
        }
    }
    else if (efiAllowUnsigned)
    {
        app.Logger.LogWarning(
            "Efi:WebhookAllowUnsigned=true em Production — /api/webhooks/pix aceita " +
            "requests sem HMAC. Configurar Efi__WebhookSecret e desativar essa flag.");
    }
}

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
