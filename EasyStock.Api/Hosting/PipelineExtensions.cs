using System.Collections.Concurrent;
using System.Reflection;
using EasyStock.Api.Configuration;
using EasyStock.Api.Middleware;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Infra.Async.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Context;

namespace EasyStock.Api.Hosting;

/// <summary>
/// Pipeline completo do <c>EasyStock.Api</c> em método único — transcrição verbatim do
/// que vivia inline no <c>Program.cs</c>:
/// <list type="number">
///   <item>ExceptionHandler (primeiro, captura tudo abaixo)</item>
///   <item>ResponseCompression + SecurityHeaders</item>
///   <item>Correlation ID middleware (X-Correlation-Id + LogContext.PushProperty)</item>
///   <item>SerilogRequestLogging (com GetLevel custom + EnrichDiagnosticContext)</item>
///   <item>Swagger JSON cache in-memory (TTL 1h)</item>
///   <item>Swagger UI (Dev/Staging ou flag)</item>
///   <item>HttpsRedirection + StaticFiles (incluindo storage local pra uploaded files)</item>
///   <item>Mobile PWA static files</item>
///   <item>Cors + RateLimiter + Authentication + Authorization</item>
///   <item>ClienteSession sliding window + SubscriptionGate + Idempotency</item>
///   <item>MapControllers + MapGet redirects (/, /console, /api-docs)</item>
///   <item>MapHealthChecks (/health, /health/live, /health/ready, /health/api, /health/dispatcher)</item>
///   <item>MapGet /health/version (PWA OTA schema gate)</item>
///   <item>Production warnings: Mobile:RequireApiKey=false, Efi WebhookSecret/AllowUnsigned</item>
/// </list>
///
/// Ordem dos middlewares é load-bearing — divergência da ordem original deve ser commit
/// separado deliberado, nunca silenciosa.
/// </summary>
public static class PipelineExtensions
{
    public static void UseEasyStockPipeline(this WebApplication app)
    {
        // ExceptionHandler deve ser o primeiro middleware para capturar exceções de qualquer
        // middleware abaixo, incluindo swagger, static files e autenticação.
        app.UseExceptionHandler();

        // ResponseCompression precisa rodar cedo, antes de StaticFiles e do request logging,
        // pra ter chance de capturar o output dos middlewares seguintes.
        app.UseResponseCompression();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Correlation ID propagation
        app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(correlationId))
                correlationId = Guid.NewGuid().ToString();

            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-Id"] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
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
            var swaggerCache = new ConcurrentDictionary<string, (byte[] Body, string ContentType, DateTimeOffset CachedAt)>();
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
                using var buffer = new MemoryStream();
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
            || app.Configuration.GetValue<bool>("Swagger:EnableInProduction");
        if (swaggerEnabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "EasyStock API v1");
                c.RoutePrefix = "swagger";
                c.DocumentTitle = "EasyStock API Docs";
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
        var fileStorageOptions = app.Configuration
            .GetSection(ConfigurationKeys.SectionFileStorage)
            .Get<FileStorageOptions>() ?? new();
        if (!string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            var localStorage = app.Services.GetRequiredService<IFileStorage>()
                as LocalFileStorage;
            if (localStorage is not null)
            {
                var rootPath = localStorage.GetRootPath();
                Directory.CreateDirectory(rootPath);

                // PublicBaseUrl pode ser absoluta (ex: https://api.host/files) para que as URLs
                // salvas no banco resolvam cross-host: a imagem e exibida na Web/Admin/PWA, que
                // rodam em outra origem e nao servem /files. O RequestPath do static-files exige
                // apenas o caminho, entao extraimos o path quando a base e uma URL absoluta.
                var servePath = fileStorageOptions.PublicBaseUrl;
                if (Uri.TryCreate(servePath, UriKind.Absolute, out var absBase))
                    servePath = absBase.AbsolutePath;
                servePath = "/" + servePath.Trim('/');

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(rootPath),
                    RequestPath = servePath
                });
            }
        }

        // Casa da Baba Mobile PWA — static files em /pwa/ com headers de service worker.
        Mobile.MobileModule.UseMobilePwa(app);

        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        // Sliding window: atualiza UltimoUsoEm da ClienteSession após cada request autenticado (ADR-0012).
        app.UseMiddleware<ClienteSessionMiddleware>();
        app.UseMiddleware<SubscriptionGateMiddleware>();
        // Idempotencia: aplicado APOS auth para que ICurrentUserAccessor.EmpresaId esteja disponivel.
        // Whitelist de POSTs criticos (R5: dedup retry de mobile/web).
        IdempotencyMiddlewareExtensions.UseIdempotency(app, opts => opts
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

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
        });

        // /health/api: dependencias HTTP da API (PG + Redis + config) — NAO inclui dispatcher.
        // Loop de notificacoes preso nao deve marcar a API inteira como down nos LBs.
        app.MapHealthChecks("/health/api", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("api"),
            ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
        });

        // /health/dispatcher: heartbeats dos 3 BackgroundServices do pipeline de notificacoes.
        // Healthy quando Mode=Disabled (pipeline em Worker separado). Unhealthy quando algum
        // loop nao bate dentro de 5x intervalo configurado — sinal de pendurada.
        app.MapHealthChecks("/health/dispatcher", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("dispatcher"),
            ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
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
    }
}
