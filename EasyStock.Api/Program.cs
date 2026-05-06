using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Data;
using EasyStock.Api.Observability;
using EasyStock.Api.Services;
using Microsoft.AspNetCore.Builder;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Services;
using EasyStock.Application.Validators;
using EasyStock.Infra.MongoDb.DependencyInjection;
using EasyStock.Infra.MongoDb.HealthChecks;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Sqlite.DependencyInjection;
using EasyStock.Infra.Sqlite.HealthChecks;
using EasyStock.Infra.Async.DependencyInjection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EasyStock.Api.Observability.HealthChecks;
using Swashbuckle.AspNetCore.Annotations;

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

// Configure Serilog — máximo de informações em cada log entry
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .Enrich.WithMachineName()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Core MVC ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

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
var mongoDatabaseName = builder.Configuration[ConfigurationKeys.DatabaseMongoDatabase] ?? "EasyStockDbMongo";
var sqliteConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionSqlite) ?? "Data Source=easystock.db";

// Em produção, pula a checagem de auto-detect (custa 3-5s no cold start)
// quando o provider está explicitamente configurado.
string resolvedProvider;
if (builder.Environment.IsProduction() &&
    !databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
{
    resolvedProvider = databaseProvider.Trim().ToLowerInvariant() switch
    {
        "postgres" or "postgresql" => "postgresql",
        "mongodb" or "mongo" => "mongodb",
        "sqlite" => "sqlite",
        _ => "postgresql"
    };
}
else
{
    resolvedProvider = await ResolveDatabaseProviderAsync(
        databaseProvider, postgresConnectionString, mongoConnectionString, Log.Logger);
}

var isFallback = !string.Equals(databaseProvider.Trim(), resolvedProvider, StringComparison.OrdinalIgnoreCase)
    && !(databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) && resolvedProvider == "postgresql");

// Fail-fast: nunca subir em produção usando SQLite (seria banco local efêmero no container)
if (resolvedProvider == "sqlite" && builder.Environment.IsProduction())
    throw new InvalidOperationException(
        "PostgreSQL indisponível e SQLite não é permitido em Production. " +
        "Verifique a connection string 'DefaultConnection' e a conectividade com o banco.");

var infraState = new ResolvedInfrastructureState
{
    DatabaseProvider = resolvedProvider,
    ConfiguredProvider = databaseProvider,
    IsFallback = isFallback,
    StartupTime = DateTimeOffset.UtcNow,
    Environment = builder.Environment.EnvironmentName
};
builder.Services.AddSingleton(infraState);

switch (resolvedProvider)
{
    case "mongodb":
        // MongoDB foi descontinuado como provedor transacional (B2 do plano de a��o).
        // Paridade incompleta com Postgres (sem Venda, ItemVenda, MovimentacaoEstoque,
        // Caixa, Lote, Pedido) gerava risco de bug silencioso. Postgres � o �nico
        // provedor transacional suportado. Rever ADR 0001-mongo-discarded.
        throw new NotSupportedException(
            "MongoDB foi descontinuado como provedor transacional. " +
            "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");

    case "postgresql":
        builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddNpgSql(postgresConnectionString!, name: "PostgreSQL", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("Redis")                          // sem tag "ready" — Redis degradado não remove pod do LB
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready"]);
        break;

    case "sqlite":
        builder.Services.AddEasyStockSqliteInfrastructure(sqliteConnectionString, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<SqliteDatabaseHealthCheck>("SQLite", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("Redis")                          // sem tag "ready"
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready"]);
        break;

    default:
        throw new InvalidOperationException($"Database:Provider '{databaseProvider}' não suportado.");
}

// ── Application + Async Infra ─────────────────────────────────────────────────
builder.Services.AddEasyStockApplication();
builder.Services.Configure<EasyStock.Application.Services.PedidoEstoqueOptions>(
    builder.Configuration.GetSection("Pedidos"));
builder.Services.AddEasyStockAsyncInfrastructure(builder.Configuration);
builder.Services.Configure<EasyStockConfiguracoes>(
    builder.Configuration.GetSection(ConfigurationKeys.SectionEasyStock));

// Registrar IEasyStockConfiguracoes para injeção em use cases
builder.Services.AddScoped<EasyStock.Application.Configuration.IEasyStockConfiguracoes>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EasyStockConfiguracoes>>().Value);

// ── Background Services + misc ────────────────────────────────────────────────
builder.Services.AddEasyStockBackgroundJobs(builder.Configuration);
builder.Services.AddHttpClient(); // for DiagnosticoInfraController self-testing
builder.Services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

// ── Mobile module services (Onda 2 parte 2: stock reconciliation) ────────────
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.MobileStockReconciler>();
// Onda 3: vendas mobile -> Venda ERP (Order entregue cria Venda + ItemVenda).
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.MobileSaleSyncService>();
// Onda 5: SSE realtime entre devices da mesma loja.
// Broker é Singleton — listeners persistem cross-request via dictionary in-memory.
// Em multi-instance, evoluir pra Redis pubsub.
builder.Services.AddSingleton<EasyStock.Api.Mobile.Services.MobileEventBroker>();

// SeedProgressService: Singleton pra compartilhar estado de runs entre requests.
// O background job e o polling endpoint falam com a mesma instância.
builder.Services.AddSingleton<EasyStock.Api.Services.SeedProgressService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Migrations + Seed (PostgreSQL only) ───────────────────────────────────────
// Em produção com múltiplas réplicas, desabilitar via RunMigrationsOnStartup=false
// e rodar migrations em init-container ou job separado antes do deploy.
var runMigrationsOnStartup = builder.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: !app.Environment.IsProduction());
if (runMigrationsOnStartup && resolvedProvider is "postgresql")
{
    try
    {
        List<string> pendingMigrations;
        using (var checkScope = app.Services.CreateScope())
        {
            var checkDb = checkScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            pendingMigrations = (await checkDb.Database.GetPendingMigrationsAsync()).ToList();
        }

        app.Logger.LogInformation("{Count} migrations pendentes.", pendingMigrations.Count);

        // Migrations conhecidas que historicamente colidem com schema mobile pré-existente
        // (porque criam tabelas que mobile schema raw também cria com IF NOT EXISTS).
        // Para essas, aceitamos 42P07/42701 e registramos manualmente. Para qualquer
        // outra migration, falha real é fail-fast.
        var migrationsComColisaoConhecida = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "20260430193546_AddAdminModule",
            "20260430210354_RenameAdminAuditLogsTable_AddMissingDbSets"
        };

        foreach (var migrationId in pendingMigrations)
        {
            try
            {
                using var migScope = app.Services.CreateScope();
                var migDb = migScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(migrationId);
                app.Logger.LogInformation("Migration {MigrationId} aplicada.", migrationId);
            }
            catch (Npgsql.PostgresException ex) when (
                ex.SqlState is "42701" or "42P07" &&
                migrationsComColisaoConhecida.Contains(migrationId))
            {
                app.Logger.LogWarning(
                    "Migration {MigrationId}: schema já existe ({SqlState}), registrando como aplicada.",
                    migrationId, ex.SqlState);
                using var regScope = app.Services.CreateScope();
                var regDb = regScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                const string productVersion = "9.0.0";
                await regDb.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {productVersion}) ON CONFLICT DO NOTHING");
            }
        }

        infraState.MigrationsApplied = true;
        app.Logger.LogInformation("Migrations aplicadas com sucesso.");
    }
    catch (Exception ex)
    {
        infraState.MigrationsApplied = false;
        infraState.MigrationError = ex.Message;
        app.Logger.LogError(ex, "Erro durante migrations. A aplicacao continuara mas pode estar incompleta.");
    }

    try
    {
        using var seedScope = app.Services.CreateScope();
        await SeedData.ExecutarAsync(seedScope.ServiceProvider, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Erro durante seed. Continuando sem seed.");
    }

    // Schema do módulo Casa da Baba Mobile (SQL raw, idempotente, fora do EF migrations).
    try
    {
        await EasyStock.Api.Mobile.Schema.MobileSchemaInitializer.InitializeAsync(app.Services, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Falha ao aplicar Mobile schema. Endpoints /api/mobile/* vão falhar.");
    }
}

// ── Startup hardening ─────────────────────────────────────────────────────────
if (resolvedProvider is "sqlite" && !app.Environment.IsDevelopment())
    app.Logger.LogWarning(
        "ATENCAO: Banco SQLite em uso em ambiente {Env}. Isso pode indicar falha de conexao com banco principal.",
        app.Environment.EnvironmentName);

var jwtSecret = builder.Configuration[ConfigurationKeys.JwtSecretKey];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("${JWT_SECRET_KEY}"))
    throw new InvalidOperationException("JWT_SECRET_KEY environment variable is required (min 32 chars). Set it before starting the API.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("JWT_SECRET_KEY must be at least 32 characters long.");

// Validar connection strings não têm placeholders
if (postgresConnectionString?.Contains("${") == true)
    throw new InvalidOperationException("Database connection string contains placeholders. Set environment variables: DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD.");

if (mongoConnectionString?.Contains("${") == true)
    throw new InvalidOperationException("MongoDB connection string contains placeholders. Set MONGO_CONNECTION_STRING environment variable.");

// Validar que database credentials não são defaults/placeholders
if (postgresConnectionString?.Contains("Username=postgres") == true && postgresConnectionString?.Contains("Password=postgres") == true)
    throw new InvalidOperationException("CRITICAL: Default PostgreSQL credentials detected. Set DB_PASSWORD to a secure value before deployment.");

Log.Information("""

    ======================================
      EasyStock API
      Ambiente:     {Environment}
      Banco:        {Provider} (configurado: {Configured})
      Fallback:     {Fallback}
      Raiz (/):     → redireciona para /swagger
      Swagger:      /swagger
      Diagnostico:  /diagnostico
      Health:       /health, /health/live, /health/ready
    ======================================
    """,
    app.Environment.EnvironmentName, resolvedProvider, databaseProvider, isFallback);

// ── Middleware pipeline ───────────────────────────────────────────────────────
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
        await next();
        buffer.Position = 0;
        var body = buffer.ToArray();
        var contentType = context.Response.ContentType ?? "application/json";
        swaggerCache[path] = (body, contentType, DateTimeOffset.UtcNow);
        context.Response.Headers["X-Swagger-Cache"] = "MISS";
        context.Response.Body = originalBody;
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
        c.SwaggerEndpoint("/swagger/v1-ptbr/swagger.json", "EasyStock API (Português BR)");
        c.SwaggerEndpoint("/swagger/v1-en/swagger.json",   "EasyStock API (English)");
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

app.UseExceptionHandler(); // deve ser o primeiro para capturar exceções de qualquer middleware abaixo
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<EasyStock.Api.Middleware.SubscriptionGateMiddleware>();
// Idempotencia: aplicado APOS auth para que ICurrentUserAccessor.EmpresaId esteja disponivel.
// Whitelist de POSTs criticos (R5: dedup retry de mobile/web).
EasyStock.Api.Middleware.IdempotencyMiddlewareExtensions.UseIdempotency(app, opts => opts
    .Add("/api/itensestoque")
    .Add("/api/itensestoque/estorno")
    .Add("/api/vendas")
    .Add("/api/mobile/vendas")
    .Add("/api/movimentacoes")
    .Add("/api/itensestoque/repor"));
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger", permanent: false))
   .ExcludeFromDescription();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckJsonResponse
});

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = WriteHealthCheckJsonResponse
});

// Aviso de seguranca em Production: se Mobile:RequireApiKey ainda esta false,
// /api/mobile/sync aceita request anonimo. Configurar a env var
// Mobile__RequireApiKey=true no App Service quando todos os APKs estiverem
// pareados (ver Mobile/DEPLOY-RUNBOOK.md, Passo 6).
if (app.Environment.IsProduction()
    && !app.Configuration.GetValue<bool>("Mobile:RequireApiKey"))
{
    app.Logger.LogWarning(
        "Mobile:RequireApiKey={Value} em Production — /api/mobile/sync aceita request anonimo. " +
        "Recomendado virar true via env var Mobile__RequireApiKey=true.",
        false);
}

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static Task WriteHealthCheckJsonResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var result = new
    {
        status = report.Status.ToString(),
        totalDuration = report.TotalDuration.TotalMilliseconds.ToString("0") + "ms",
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds.ToString("0") + "ms",
            error = e.Value.Exception?.Message
        })
    };
    return context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
}

static async Task<string> ResolveDatabaseProviderAsync(
    string configuredProvider,
    string? postgresConnectionString,
    string? mongoConnectionString,
    Serilog.ILogger logger)
{
    var normalized = configuredProvider.Trim().ToLowerInvariant();

    if (normalized is "sqlite")
        return "sqlite";

    if (normalized is "postgres" or "postgresql")
    {
        if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
            await IsPostgresAvailableAsync(postgresConnectionString, logger))
            return "postgresql";

        logger.Warning(
            "PostgreSQL não disponível (connection string: {HasCs}). Usando SQLite como fallback.",
            !string.IsNullOrWhiteSpace(postgresConnectionString));
        return "sqlite";
    }

    if (normalized is "mongodb" or "mongo")
    {
        // B2: Mongo descontinuado como provedor transacional. Falha r�pido para
        // operador notar que precisa migrar para Postgres.
        throw new NotSupportedException(
            "MongoDB foi descontinuado como provedor transacional. " +
            "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");
    }

    if (normalized is "auto")
    {
        if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
            await IsPostgresAvailableAsync(postgresConnectionString, logger))
        {
            logger.Information("Auto-deteccao: usando PostgreSQL.");
            return "postgresql";
        }

        // Auto n�o cai mais em Mongo (B2). PostgreSQL indispon�vel + Auto = SQLite (dev only).
        logger.Warning("Auto-deteccao: PostgreSQL indispon�vel. Usando SQLite (dev/fallback).");
        return "sqlite";
    }

    throw new InvalidOperationException($"Database:Provider '{configuredProvider}' não suportado.");
}

static async Task<bool> IsPostgresAvailableAsync(string connectionString, Serilog.ILogger logger)
{
    try
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 3,
            CommandTimeout = 3
        };
        await using var conn = new Npgsql.NpgsqlConnection(csb.ToString());
        await conn.OpenAsync();
        return true;
    }
    catch (Exception ex)
    {
        logger.Debug(ex, "PostgreSQL indisponivel.");
        return false;
    }
}

public partial class Program { }
