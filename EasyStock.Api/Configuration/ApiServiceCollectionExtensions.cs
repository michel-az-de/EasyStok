using EasyStock.Api.Authorization;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Infra.Async.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
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
            // ── Single API document (idioma pt-BR — único do produto) ────────
            c.SwaggerDoc("v1", SwaggerConfiguration.Info);

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
        services.AddSingleton<EasyStock.Api.Services.Impressao.PedidoImpressoHtml>(); // S49: impresso do pedido
        services.AddSingleton<EasyStock.Api.Services.Impressao.ComandaHtml>(); // S52: comanda de cozinha
        services.AddScoped<EasyStock.Api.Services.IJwtTokenService, JwtTokenService>();
        services.AddScoped<EasyStock.Application.Ports.Output.IJwtTokenService>(sp =>
            sp.GetRequiredService<EasyStock.Api.Services.IJwtTokenService>());

        // #1352: sessão revogável. O validador roda no OnTokenValidated do JwtBearer e no laço do SSE.
        services.AddScoped<EasyStock.Api.Authentication.ValidadorSessaoUsuario>();

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

                // #1352: o JWT passa a ser revogável. Token emitido antes do corte de sessão do usuário, ou de
                // conta inativa, falha aqui com 401; Auth:SessoesRevogaveis=false desliga a checagem.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = EasyStock.Api.Authentication.SessaoRevogavel.AoValidarTokenAsync,
                };
            })
            .AddInternalCronJobScheme(configuration)
            // S20: bridge de impressão (X-Impressao-Api-Key), só nas rotas da policy ImpressaoFila.
            .AddImpressaoApiKeyScheme(configuration)
            // Sessões server-side do storefront (ADR-0012) — cookie __Host-cdb_session
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                EasyStock.Api.Authentication.ClienteSessionAuthenticationHandler>(
                EasyStock.Api.Authentication.ClienteSessionAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorization(opts =>
        {
            opts.AddPolicy("SuperAdmin", p => p.RequireClaim("nivel", "SuperAdmin"));
            opts.AddPolicy("Admin",    p => p.RequireClaim("nivel", "SuperAdmin", "Admin"));
            opts.AddPolicy("Gerente",  p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente"));
            opts.AddPolicy("Operador", p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente", "Operador"));
            opts.AddInternalCronJobPolicy();
            opts.AddImpressaoFilaPolicy();
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

            // Rate limit para endpoints sensíveis de autenticação (login,
            // forgot/reset password). Particionado por IP do cliente para dificultar
            // brute-force e spam de contas. O IP real volta a ser confiavel apos o fix do
            // ForwardedHeaders (Program.cs) + repasse de X-Forwarded-For pelos BFFs Web/Admin;
            // antes disso TODOS colapsavam no IP do container e 10/min travava o login geral.
            options.AddPolicy("auth", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Refresh de token NAO e vetor de brute-force (exige um refresh token valido) e
            // e chamado com frequencia pelos handlers de sessao dos BFFs (restore + retry em
            // 401). Mante-lo no balde "auth" de login fazia o refresh esfomear o login -> 429
            // (incidente login-admin). Balde proprio e generoso, particionado por IP real.
            options.AddPolicy("auth-refresh", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    });
            });

            // FAQ publico — feedback POST. Limite mais apertado.
            options.AddPolicy("public-post", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Webhooks da Meta (WhatsApp, Instagram, Messenger). A Meta entrega em rajada a
            // partir de poucos IPs; 429 faz ela reenviar e, insistindo, desativar a assinatura.
            // O endpoint ja exige HMAC (X-Hub-Signature-256), entao aqui e so teto contra flood
            // nao autenticado. Particionado por IP, generoso (issue 1105).
            options.AddPolicy("webhook-meta", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Rate limit pro signup de empresa nova. Particionado por IP pra
            // travar criacao em massa de tenants falsos. 5/IP/hora chega pra
            // demos pessoais e onboarding de cliente novo, com folga.
            options.AddPolicy("signup", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Rate limit pra checagens de disponibilidade (email/CNPJ) feitas
            // inline durante signup. 30/IP/min cobre digitacao com debounce.
            options.AddPolicy("disponibilidade", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Chat do site (S36), público e anônimo. Abrir sessão: por IP, apertado (cada sessão é
            // uma linha no banco). Mandar mensagem: por sessão (hash do token), para um visitante não
            // gastar a cota do outro atrás do mesmo NAT; sem token, cai no IP. Ler: por IP, folgado
            // (o widget lê ao reconectar o stream).
            options.AddPolicy(ChatSiteRateLimit.AbrirSessao, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(ChatSiteRateLimit.Mensagem, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ChatSiteRateLimit.ChaveMensagem(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = ChatSiteRateLimit.MensagensPorMinuto, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Identificação do visitante (#1430): por sessão, como a mensagem, e bem mais apertada.
            options.AddPolicy(ChatSiteRateLimit.Identificacao, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ChatSiteRateLimit.ChaveMensagem(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = ChatSiteRateLimit.IdentificacoesPorMinuto, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(ChatSiteRateLimit.Leitura, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;            options.OnRejected = async (context, cancellationToken) =>
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
        services.AddSingleton<IOperationalMetrics>(sp => sp.GetRequiredService<MetricsService>());
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // string.IsNullOrWhiteSpace (nao `??`): o compose da VM injeta a env como string
        // vazia quando OTEL_COLLECTOR_ENDPOINT nao esta no .env, e new Uri("") lanca
        // UriFormatException derrubando a API no startup (issue 1002).
        var otlpConfigured = configuration[ConfigurationKeys.OtlpEndpoint];
        var otlpEndpoint = new Uri(string.IsNullOrWhiteSpace(otlpConfigured) ? "http://localhost:4317" : otlpConfigured);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("EasyStock.Api"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddOtlpExporter(options => options.Endpoint = otlpEndpoint);

                if (environment.IsDevelopment())
                {
                    tracing.AddConsoleExporter();
                }
                else
                {
                    // Em prod, amostra 10% das traces pra reduzir custo de egress
                    // OTLP e backend (Honeycomb/Tempo cobram por span). Health checks
                    // e polling endpoints ja sao filtrados pelo Serilog request logging.
                    // ParentBased: quando a Web/Admin ja decidiu amostrar o trace, a API
                    // segue a decisao — sem isso o ratio re-decide e quebra o trace
                    // distribuido Web -> Api -> Postgres no meio (issue 1002).
                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MetricNames.MeterName)
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
        // IFileStorage (provider switch Local/S3) vive em Infra.Async.Storage,
        // compartilhado com o Worker — que também precisa dele para o motor de relatórios.
        services.AddEasyStockFileStorageCore(configuration);

        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        return services;
    }
}
