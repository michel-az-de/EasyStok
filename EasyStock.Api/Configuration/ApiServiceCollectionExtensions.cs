using EasyStock.Api.Http;
using EasyStock.Api.Observability;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;
using System.Threading.RateLimiting;

namespace EasyStock.Api.Configuration;

/// <summary>
/// Extension methods that group related DI registrations, keeping Program.cs focused
/// on startup orchestration rather than service wiring detail.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    // ── Swagger ──────────────────────────────────────────────────────────────

    public static IServiceCollection AddEasyStockSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            // ── Two language documents ────────────────────────────────────────
            c.SwaggerDoc("v1-ptbr", SwaggerConfiguration.InfoPortuguese);
            c.SwaggerDoc("v1-en",   SwaggerConfiguration.InfoEnglish);

            // ── Security ──────────────────────────────────────────────────────
            c.AddSecurityDefinition("Bearer", SwaggerConfiguration.BearerScheme);
            c.AddSecurityRequirement(SwaggerConfiguration.BearerRequirement);

            // ── Annotations ───────────────────────────────────────────────────
            c.EnableAnnotations();

            // ── XML comments ──────────────────────────────────────────────────
            SwaggerXmlExtensions.IncludeXmlComments(c);

            // ── Schema & operation filters ────────────────────────────────────
            c.SchemaFilter<SchemaExamplesFilter>();
            c.OperationFilter<GetOperationExamplesFilter>();
            c.DocumentFilter<TagDescriptionsDocumentFilter>();

            // ── Use fully-qualified names to avoid schema conflicts ───────────
            c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));

            // ── Order operations alphabetically by path ───────────────────────
            c.OrderActionsBy(api => $"{api.GroupName}_{api.RelativePath}");
        });

        return services;
    }

    // ── Authentication / Authorization ───────────────────────────────────────

    public static IServiceCollection AddEasyStockAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration[ConfigurationKeys.JwtSecretKey]
            ?? throw new InvalidOperationException(
                "Jwt:SecretKey não configurado. Defina a variavel de ambiente 'Jwt__SecretKey' ou configure appsettings.Development.json.");

        var jwtIssuer = configuration[ConfigurationKeys.JwtIssuer]
            ?? throw new InvalidOperationException(
                "Jwt:Issuer não configurado. Sem ele, todos os tokens JWT serão rejeitados em produção.");

        var jwtAudience = configuration[ConfigurationKeys.JwtAudience]
            ?? throw new InvalidOperationException(
                "Jwt:Audience não configurado. Sem ele, todos os tokens JWT serão rejeitados em produção.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<AdminAuditService>();
        services.AddScoped<GeradorNotificacoesAutomaticas>();
        services.AddScoped<EasyStock.Api.Services.IJwtTokenService, JwtTokenService>();
        services.AddScoped<EasyStock.Application.Ports.Output.IJwtTokenService>(sp =>
            sp.GetRequiredService<EasyStock.Api.Services.IJwtTokenService>());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Desabilita o remap legacy de claims do JWT (sub -> nameidentifier, etc).
                // Sem isso, CurrentUserAccessor.FindFirstValue("sub") retorna null porque
                // o handler renomeia "sub" para ClaimTypes.NameIdentifier por default.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey  = true,
                    ValidIssuer              = jwtIssuer,
                    ValidAudience            = jwtAudience,
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    NameClaimType            = "sub",
                    RoleClaimType            = "nivel"
                };
            });

        services.AddAuthorization(opts =>
        {
            opts.AddPolicy("SuperAdmin", p => p.RequireClaim("nivel", "SuperAdmin"));
            opts.AddPolicy("Admin",    p => p.RequireClaim("nivel", "SuperAdmin", "Admin"));
            opts.AddPolicy("Gerente",  p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente"));
            opts.AddPolicy("Operador", p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente", "Operador"));
        });

        return services;
    }

    // ── CORS ─────────────────────────────────────────────────────────────────

    public static IServiceCollection AddEasyStockCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = configuration.GetSection(ConfigurationKeys.CorsAllowedOrigins).Get<string[]>();
                if (allowedOrigins is { Length: > 0 })
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                else if (environment.IsDevelopment())
                {
                    Serilog.Log.Warning("⚠️  CORS AllowAnyOrigin ativo — não use em produção!");
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
                else
                    throw new InvalidOperationException(
                        "Cors:AllowedOrigins é obrigatório em produção. Configure a secao 'Cors:AllowedOrigins' no appsettings ou via variavel de ambiente.");
            });
        });

        return services;
    }

    // ── Rate Limiting ─────────────────────────────────────────────────────────

    public static IServiceCollection AddEasyStockRateLimit(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
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

            // Onda 7 — Rate limit pra endpoints mobile anônimos.
            // Cobre: GET /api/mobile/version, POST /api/mobile/devices/pair,
            // POST /api/mobile/diagnostics/errors. Particionado por IP pra
            // evitar abuso (tentativa de adivinhar pairing code, flood).
            // Limites generosos pra não atrapalhar uso legítimo (PWA pinga
            // version no boot a cada reload).
            options.AddPolicy("mobile-anonymous", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    });
            });

            // Rate limit para endpoints sensíveis de autenticação (login, register,
            // forgot/reset password). Particionado por IP do cliente para dificultar
            // brute-force e spam de contas.
            options.AddPolicy("auth", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
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
                var correlationId = context.HttpContext.Items["CorrelationId"] as string
                    ?? context.HttpContext.TraceIdentifier;
                var envelope = new ApiErrorResponse(
                    new ApiError(
                        "RATE_LIMIT_EXCEEDED",
                        "Muitas requisicoes",
                        "Limite de requisicoes atingido. Tente novamente mais tarde.",
                        correlationId));
                await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
            };
        });

        return services;
    }

    // ── Observability (OpenTelemetry + Exception Handler) ────────────────────

    public static IServiceCollection AddEasyStockObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddSingleton<MetricsService>();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        var otlpEndpoint = new Uri(configuration[ConfigurationKeys.OtlpEndpoint] ?? "http://localhost:4317");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("EasyStock.Api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = otlpEndpoint);

                if (environment.IsDevelopment())
                    tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = otlpEndpoint);

                if (environment.IsDevelopment())
                    metrics.AddConsoleExporter();
            });

        return services;
    }

    // ── Distributed Cache ────────────────────────────────────────────────────

    public static IServiceCollection AddEasyStockCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        var redisConnectionString = configuration.GetConnectionString(ConfigurationKeys.ConnectionRedis);
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }

    // ── File Storage ─────────────────────────────────────────────────────────

    public static IServiceCollection AddEasyStockFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(ConfigurationKeys.SectionFileStorage));

        var fileStorageOptions = configuration
            .GetSection(ConfigurationKeys.SectionFileStorage)
            .Get<FileStorageOptions>() ?? new FileStorageOptions();

        if (string.Equals(fileStorageOptions.Provider, "AzureFileShare", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, AzureFileShareStorage>();
        else if (string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, S3CompatibleFileStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        return services;
    }
}
