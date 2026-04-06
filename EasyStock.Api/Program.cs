using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Validators;
using EasyStock.Application.Ports.Output;
using EasyStock.Infra.MongoDb.DependencyInjection;
using EasyStock.Infra.MongoDb.HealthChecks;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Sqlite.DependencyInjection;
using EasyStock.Infra.Sqlite.HealthChecks;
using EasyStock.Infra.Async.DependencyInjection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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
using Swashbuckle.AspNetCore.Annotations;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
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

    // ── Use fully-qualified names to avoid schema conflicts ───────────────
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));

    // ── Order operations alphabetically by path ───────────────────────────
    c.OrderActionsBy(api => $"{api.GroupName}_{api.RelativePath}");
});

// Database
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Auto";
var postgresConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoConnection");
var mongoDatabaseName = builder.Configuration["Database:MongoDatabase"] ?? "EasyStockDbMongo";
var sqliteConnectionString = builder.Configuration.GetConnectionString("SqliteConnection") ?? "Data Source=easystock.db";

var resolvedProvider = await ResolveDatabaseProviderAsync(
    databaseProvider, postgresConnectionString, mongoConnectionString, Log.Logger);

switch (resolvedProvider)
{
    case "mongodb":
        builder.Services.AddEasyStockMongoInfrastructure(mongoConnectionString!, mongoDatabaseName, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<MongoDatabaseHealthCheck>("MongoDB");
        break;

    case "postgresql":
        builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddNpgSql(postgresConnectionString!, name: "PostgreSQL");
        break;

    case "sqlite":
        builder.Services.AddEasyStockSqliteInfrastructure(sqliteConnectionString, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<SqliteDatabaseHealthCheck>("SQLite");
        break;

    default:
        throw new InvalidOperationException($"Database:Provider '{databaseProvider}' nao suportado.");
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
            "PostgreSQL nao disponivel (connection string: {HasCs}). Usando SQLite como fallback.",
            !string.IsNullOrWhiteSpace(postgresConnectionString));
        return "sqlite";
    }

    if (normalized is "mongodb" or "mongo")
    {
        if (!string.IsNullOrWhiteSpace(mongoConnectionString) &&
            await IsMongoAvailableAsync(mongoConnectionString, logger))
            return "mongodb";

        logger.Warning(
            "MongoDB nao disponivel (connection string: {HasCs}). Usando SQLite como fallback.",
            !string.IsNullOrWhiteSpace(mongoConnectionString));
        return "sqlite";
    }

    if (normalized is "auto" or "")
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

    throw new InvalidOperationException($"Database:Provider '{configuredProvider}' nao suportado.");
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
    builder.Configuration.GetSection("EasyStock"));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));

var fileStorageOptions = builder.Configuration.GetSection("FileStorage").Get<FileStorageOptions>() ?? new FileStorageOptions();
if (string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileStorage, S3CompatibleFileStorage>();
}
else
{
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
}

// Observability
builder.Services.AddSingleton<EasyStock.Api.Observability.MetricsService>();
builder.Services.AddProblemDetails();

// Authentication / Authorization
var jwtKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

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
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
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
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is { Length: > 0 })
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
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
builder.Services.AddHostedService<AnalisadorEstoqueBackgroundService>();

// Exception Handler
builder.Services.AddExceptionHandler<EasyStock.Api.Observability.GlobalExceptionHandler>();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("EasyStock.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        })
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        })
        .AddConsoleExporter());

builder.Services.AddMemoryCache();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
});

builder.Services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

var app = builder.Build();

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
    await next();
});

// Serilog Request Logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        diagnosticContext.Set("Endpoint", httpContext.Request.Path);
        diagnosticContext.Set("MetodoHttp", httpContext.Request.Method);
    };
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
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
}

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
app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler();
app.MapControllers();

// Health Check endpoint
app.MapHealthChecks("/health");

app.Run();

public partial class Program
{
}
