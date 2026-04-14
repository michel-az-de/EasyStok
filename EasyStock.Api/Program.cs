using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Data;
using EasyStock.Api.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Validators;
using EasyStock.Application.Ports.Output;
using EasyStock.Infra.MongoDb.DependencyInjection;
using EasyStock.Infra.MongoDb.HealthChecks;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Sqlite.DependencyInjection;
using EasyStock.Infra.Sqlite.HealthChecks;
using EasyStock.Infra.Async.DependencyInjection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EasyStock.Api.Observability;
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

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddEndpointsApiExplorer();

// Swagger com suporte a JWT Bearer, dois idiomas e exemplos
builder.Services.AddSwaggerGen(c =>
{
    // ── Two language documents ────────────────────────────────────────────
    c.SwaggerDoc("v1-ptbr", SwaggerConfiguration.InfoPortuguese);
    c.SwaggerDoc("v1-en",   SwaggerConfiguration.InfoEnglish);

    // ── Security ──────────────────────────────────────────────────────────
    c.AddSecurityDefinition("Bearer", SwaggerConfiguration.BearerScheme);
    c.AddSecurityRequirement(SwaggerConfiguration.BearerRequirement);

    // ── Annotations ───────────────────────────────────────────────────────
    c.EnableAnnotations();

    // ── XML comments ──────────────────────────────────────────────────────
    SwaggerXmlExtensions.IncludeXmlComments(c);

    // ── Schema & operation filters ────────────────────────────────────────
    c.SchemaFilter<SchemaExamplesFilter>();
    c.OperationFilter<GetOperationExamplesFilter>();
    c.DocumentFilter<TagDescriptionsDocumentFilter>();

    // ── Use fully-qualified names to avoid schema conflicts ───────────────
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));

    // ── Order operations alphabetically by path ───────────────────────────
    c.OrderActionsBy(api => $"{api.GroupName}_{api.RelativePath}");
});

// Database
var databaseProvider = builder.Configuration[ConfigurationKeys.DatabaseProvider] ?? "Auto";
var postgresConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionDefault);
var mongoConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionMongo);
var mongoDatabaseName = builder.Configuration[ConfigurationKeys.DatabaseMongoDatabase] ?? "EasyStockDbMongo";
var sqliteConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionSqlite) ?? "Data Source=easystock.db";

var resolvedProvider = await ResolveDatabaseProviderAsync(
    databaseProvider, postgresConnectionString, mongoConnectionString, Log.Logger);

var isFallback = !string.Equals(databaseProvider.Trim(), resolvedProvider, StringComparison.OrdinalIgnoreCase)
    && !(databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) && resolvedProvider == "postgresql");

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
        builder.Services.AddEasyStockMongoInfrastructure(mongoConnectionString!, mongoDatabaseName, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<MongoDatabaseHealthCheck>("MongoDB", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("Redis", tags: ["ready"])
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready"]);
        break;

    case "postgresql":
        builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddNpgSql(postgresConnectionString!, name: "PostgreSQL", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("Redis", tags: ["ready"])
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready"]);
        break;

    case "sqlite":
        builder.Services.AddEasyStockSqliteInfrastructure(sqliteConnectionString, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<SqliteDatabaseHealthCheck>("SQLite", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("Redis", tags: ["ready"])
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready"]);
        break;

    default:
        throw new InvalidOperationException($"Database:Provider '{databaseProvider}' não suportado.");
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
        if (!string.IsNullOrWhiteSpace(mongoConnectionString) &&
            await IsMongoAvailableAsync(mongoConnectionString, logger))
            return "mongodb";

        logger.Warning(
            "MongoDB não disponível (connection string: {HasCs}). Usando SQLite como fallback.",
            !string.IsNullOrWhiteSpace(mongoConnectionString));
        return "sqlite";
    }

    if (normalized is "auto")
    {
        if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
            await IsPostgresAvailableAsync(postgresConnectionString, logger))
        {
            logger.Information("Auto-deteccao: usando PostgreSQL.");
            return "postgresql";
        }

        if (!string.IsNullOrWhiteSpace(mongoConnectionString) &&
            await IsMongoAvailableAsync(mongoConnectionString, logger))
        {
            logger.Information("Auto-deteccao: usando MongoDB.");
            return "mongodb";
        }

        logger.Warning("Auto-deteccao: nenhum banco externo disponivel. Usando SQLite.");
        return "sqlite";
    }

    throw new InvalidOperationException($"Database:Provider '{configuredProvider}' não suportado.");
}

static async Task<bool> IsPostgresAvailableAsync(string connectionString, Serilog.ILogger logger)
{
    try
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 3,
            CommandTimeout = 3
        };
        await using var conn = new Npgsql.NpgsqlConnection(builder.ToString());
        await conn.OpenAsync();
        return true;
    }
    catch (Exception ex)
    {
        logger.Debug(ex, "PostgreSQL indisponivel.");
        return false;
    }
}

static async Task<bool> IsMongoAvailableAsync(string connectionString, Serilog.ILogger logger)
{
    try
    {
        var settings = MongoDB.Driver.MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
        var client = new MongoDB.Driver.MongoClient(settings);
        await client.ListDatabaseNamesAsync();
        return true;
    }
    catch (Exception ex)
    {
        logger.Debug(ex, "MongoDB indisponivel.");
        return false;
    }
}

// Application
builder.Services.AddEasyStockApplication();

// Async Infrastructure
builder.Services.AddEasyStockAsyncInfrastructure(builder.Configuration);

// Configuration
builder.Services.Configure<EasyStockConfiguracoes>(
    builder.Configuration.GetSection(ConfigurationKeys.SectionEasyStock));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(ConfigurationKeys.SectionFileStorage));

var fileStorageOptions = builder.Configuration.GetSection(ConfigurationKeys.SectionFileStorage).Get<FileStorageOptions>() ?? new FileStorageOptions();
if (string.Equals(fileStorageOptions.Provider, "AzureFileShare", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileStorage, AzureFileShareStorage>();
}
else if (string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileStorage, S3CompatibleFileStorage>();
}
else
{
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
}

builder.Services.AddSingleton<IImageProcessor, EasyStock.Api.Services.SkiaImageProcessor>();

// Observability
builder.Services.AddSingleton<EasyStock.Api.Observability.MetricsService>();
builder.Services.AddProblemDetails();

// Authentication / Authorization
var jwtKey = builder.Configuration[ConfigurationKeys.JwtSecretKey];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:SecretKey não configurado. Defina a variavel de ambiente 'Jwt__SecretKey' ou configure appsettings.Development.json.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<GeradorNotificacoesAutomaticas>();
builder.Services.AddScoped<EasyStock.Api.Services.IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<EasyStock.Application.Ports.Output.IJwtTokenService>(sp =>
    sp.GetRequiredService<EasyStock.Api.Services.IJwtTokenService>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration[ConfigurationKeys.JwtIssuer],
            ValidAudience = builder.Configuration[ConfigurationKeys.JwtAudience],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("Admin",    p => p.RequireClaim("nivel", "SuperAdmin", "Admin"));
    opts.AddPolicy("Gerente",  p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente"));
    opts.AddPolicy("Operador", p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente", "Operador"));
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection(ConfigurationKeys.CorsAllowedOrigins).Get<string[]>();
        if (allowedOrigins is { Length: > 0 })
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else if (builder.Environment.IsDevelopment())
        {
            Log.Warning("⚠️  CORS AllowAnyOrigin ativo — não use em produção!");
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
            throw new InvalidOperationException(
                "Cors:AllowedOrigins é obrigatório em produção. Configure a secao 'Cors:AllowedOrigins' no appsettings ou via variavel de ambiente.");
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("ai", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 2;
    });
    options.AddFixedWindowLimiter("geral", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 20;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
            context.HttpContext.Response.Headers["X-RateLimit-Reset"] = ((int)retryAfter.TotalSeconds).ToString();
        }
        else
        {
            context.HttpContext.Response.Headers["Retry-After"] = "60";
            context.HttpContext.Response.Headers["X-RateLimit-Reset"] = "60";
        }

        context.HttpContext.Response.ContentType = "application/json";
        var correlationId = context.HttpContext.Items["CorrelationId"] as string ?? context.HttpContext.TraceIdentifier;
        var envelope = new EasyStock.Api.Http.ApiErrorResponse(
            new EasyStock.Api.Http.ApiError(
                "RATE_LIMIT_EXCEEDED",
                "Muitas requisicoes",
                "Limite de requisicoes atingido. Tente novamente mais tarde.",
                correlationId));
        await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
    };
});

// Background Services
builder.Services.AddEasyStockBackgroundJobs(builder.Configuration);

// HttpClient para diagnóstico de endpoints (self-testing)
builder.Services.AddHttpClient();

// Exception Handler
builder.Services.AddExceptionHandler<EasyStock.Api.Observability.GlobalExceptionHandler>();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("EasyStock.Api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration[ConfigurationKeys.OtlpEndpoint] ?? "http://localhost:4317");
            });
        if (builder.Environment.IsDevelopment())
            tracing.AddConsoleExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration[ConfigurationKeys.OtlpEndpoint] ?? "http://localhost:4317");
            });
        if (builder.Environment.IsDevelopment())
            metrics.AddConsoleExporter();
    });

builder.Services.AddMemoryCache();

var redisConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionRedis);
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

var app = builder.Build();

// Migration automática e seed de dados (somente PostgreSQL)
if (resolvedProvider is "postgresql")
{
    // Migrations — cada uma em scope isolado para tolerar conflitos de schema
    try
    {
        List<string> pendingMigrations;
        using (var checkScope = app.Services.CreateScope())
        {
            var checkDb = checkScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            pendingMigrations = (await checkDb.Database.GetPendingMigrationsAsync()).ToList();
        }

        app.Logger.LogInformation("{Count} migrations pendentes.", pendingMigrations.Count);

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
            catch (Npgsql.PostgresException ex) when (ex.SqlState is "42701" or "42P07")
            {
                // Schema já existe: registrar como aplicada sem executar
                app.Logger.LogWarning(
                    "Migration {MigrationId}: schema já existe ({SqlState}), registrando.",
                    migrationId, ex.SqlState);
                using var regScope = app.Services.CreateScope();
                var regDb = regScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                await regDb.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
                    migrationId, "9.0.0");
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

    // Seed separado para não afetar o status de migrations
    try
    {
        using var seedScope = app.Services.CreateScope();
        await SeedData.ExecutarAsync(seedScope.ServiceProvider, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Erro durante seed. Continuando sem seed.");
    }
}

// Startup hardening
if (resolvedProvider is "sqlite" && !app.Environment.IsDevelopment())
    app.Logger.LogWarning("ATENCAO: Banco SQLite em uso em ambiente {Env}. Isso pode indicar falha de conexao com banco principal.", app.Environment.EnvironmentName);

if (jwtKey.Length < 32)
    throw new InvalidOperationException(
        "JWT_SECRET deve ter pelo menos 32 caracteres.");

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

// Security headers
app.UseMiddleware<EasyStock.Api.Middleware.SecurityHeadersMiddleware>();

// Middleware for Correlation ID
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
    }
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
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";

    // Suprimir logs de endpoints de infraestrutura (health checks, ping, swagger, arquivos estáticos)
    // — esses são chamados constantemente e não carregam informação de negócio
    options.GetLevel = (ctx, _, ex) =>
    {
        if (ex is not null || ctx.Response.StatusCode >= 500)
            return Serilog.Events.LogEventLevel.Error;

        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/diagnostico/ping", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
            return Serilog.Events.LogEventLevel.Debug; // suprimido — Debug está desativado em produção

        return Serilog.Events.LogEventLevel.Information;
    };

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
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

// Middleware de cache em memória para os JSON docs do Swagger (TTL 1h, evita ~1900ms por request)
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

// Swagger disponivel em todos os ambientes para facilitar operacao
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // ── Two language documents ────────────────────────────────────────
    c.SwaggerEndpoint("/swagger/v1-ptbr/swagger.json", "EasyStock API (Português BR)");
    c.SwaggerEndpoint("/swagger/v1-en/swagger.json",   "EasyStock API (English)");

    // ── Custom route ──────────────────────────────────────────────────
    c.RoutePrefix = "swagger";

    // ── UI Behaviour ──────────────────────────────────────────────────
    c.DocumentTitle        = "EasyStock API Docs";
    c.DefaultModelsExpandDepth(1);       // show models collapsed by default
    c.DefaultModelExpandDepth(3);        // but expand 3 levels when opened
    c.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
    c.DisplayRequestDuration();
    c.EnableDeepLinking();
    c.EnableFilter();
    c.EnablePersistAuthorization();
    c.EnableTryItOutByDefault();         // open "Try it out" by default on GET ops
    c.ShowExtensions();
    c.ShowCommonExtensions();

    // ── Custom assets ─────────────────────────────────────────────────
    c.InjectStylesheet("/swagger-ui/custom.css");
    c.InjectJavascript("/swagger-ui/custom.js");
});

app.UseHttpsRedirection();

// Serve custom Swagger UI assets (CSS/JS) from wwwroot/swagger-ui
app.UseStaticFiles(); // serves wwwroot by default
if (!string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    var localStorage = app.Services.GetRequiredService<IFileStorage>() as LocalFileStorage;
    if (localStorage is not null)
    {
        var rootPath = localStorage.GetRootPath();
        Directory.CreateDirectory(rootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(rootPath),
            RequestPath = fileStorageOptions.PublicBaseUrl
        });
    }
}
app.UseCors();
app.UseRateLimiter();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Redirect root to Swagger UI for immediate discoverability
app.MapGet("/", () => Results.Redirect("/swagger", permanent: false))
   .ExcludeFromDescription();

// Health Check endpoints
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // nenhum check - apenas verifica se o processo esta vivo
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

app.Run();

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

public partial class Program
{
}
