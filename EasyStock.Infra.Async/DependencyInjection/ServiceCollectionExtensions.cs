using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pagamentos;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.Infra.Async.Pdf;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockAsyncInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Password Hasher (BCrypt). Stateless, singleton.
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        // Cache Service. <see cref="RedisCacheService"/> envelopa
        // <see cref="IDistributedCache"/> — quem registra o backend (Redis real
        // em prod ou MemoryDistributedCache em dev) e o host (ApiServiceCollectionExtensions
        // .AddEasyStockCache). <see cref="InMemoryCacheService"/> existe como
        // alternativa explicita pra fluxos in-process sem serializacao JSON.
        services.AddSingleton<ICacheService, RedisCacheService>();

        // Queue Service
        services.AddSingleton<IQueueService, BackgroundQueueService>();

        // Email Service — Onda 1.3: switch por Email:Provider em {smtp, sendgrid, console}.
        // Compat: se Email:Provider nao setado, mantem comportamento legado (Smtp se existir, senao console).
        var emailProvider = (configuration["Email:Provider"] ?? "").Trim().ToLowerInvariant();
        var smtpConfig = configuration.GetSection("Smtp");
        var sendGridConfig = configuration.GetSection("SendGrid");

        if (string.IsNullOrEmpty(emailProvider))
            emailProvider = smtpConfig.Exists() ? "smtp" : "console";

        switch (emailProvider)
        {
            case "sendgrid":
                services.AddSingleton<IEmailService>(_ => new SendGridEmailService(
                    apiKey: sendGridConfig["ApiKey"]
                        ?? throw new InvalidOperationException("SendGrid:ApiKey eh obrigatorio quando Email:Provider=sendgrid."),
                    fromEmail: sendGridConfig["FromEmail"] ?? "noreply@easystock.com",
                    fromName: sendGridConfig["FromName"] ?? "EasyStock",
                    sandbox: bool.Parse(sendGridConfig["SandboxMode"] ?? "false")));
                break;

            case "smtp":
                services.AddSingleton<IEmailService>(_ => new SmtpEmailService(
                    smtpConfig["Host"] ?? "localhost",
                    int.Parse(smtpConfig["Port"] ?? "587"),
                    smtpConfig["Username"] ?? "",
                    smtpConfig["Password"] ?? "",
                    smtpConfig["FromEmail"] ?? "noreply@easystock.com",
                    smtpConfig["FromName"] ?? "EasyStock",
                    bool.Parse(smtpConfig["EnableSsl"] ?? "true")));
                break;

            default:
                // Console — fallback dev. Loga email sem enviar.
                services.AddSingleton<IEmailService, ConsoleEmailService>();
                break;
        }

        // Storage Service
        services.AddSingleton<IStorageService, S3StorageService>();

        // PDF Renderer (Modulo Financeiro F4) — QuestPDF stateless + threadsafe
        services.AddSingleton<IFaturaPdfRenderer, FaturaPdfRenderer>();
        // PDF de etiqueta + Nota de Entrada (P5) — QuestPDF + QRCoder, stateless/threadsafe
        services.AddSingleton<IDocumentoEntradaPdfRenderer, DocumentoEntradaPdfRenderer>();
        // PDF do extrato de fechamento de caixa (#642) — QuestPDF stateless/threadsafe
        services.AddSingleton<IFechamentoCaixaExtratoRenderer, FechamentoCaixaExtratoRenderer>();

        // Modulo Financeiro F3 — abstracao multi-gateway de pagamento.
        // Adapters base sao Scoped (consomem repos scoped via DI). Cada IPagamentoGateway
        // resolvido pelo router e envolvido por MeasuredPagamentoGatewayDecorator que mede
        // latencia, classifica excecoes e alimenta IGatewayHealthStore.

        // Concretes (registrados por tipo, sem expor como IPagamentoGateway)
        services.AddScoped<EfiPixGatewayAdapter>();
        services.AddScoped<ManualGatewayAdapter>();

        // IPagamentoGateway = decorator(adapter). Adicionamos um por adapter ativo.
        services.AddScoped<IPagamentoGateway>(sp => DecorateGateway(sp, sp.GetRequiredService<EfiPixGatewayAdapter>()));
        services.AddScoped<IPagamentoGateway>(sp => DecorateGateway(sp, sp.GetRequiredService<ManualGatewayAdapter>()));

        services.AddScoped<IPagamentoGatewayRouter, PagamentoGatewayRouter>();

        // Onda P0 Payment Orchestration
        services.AddSingleton<IGatewayHealthStore, NoopGatewayHealthStore>();
        services.AddSingleton<IGatewayErrorClassifier, GatewayErrorClassifier>();
        services.AddScoped<IPagamentoOrchestrator, PagamentoOrchestrator>();

        // Webhook processors (scoped — consomem RegistrarPagamentoFaturaUseCase + repos)
        services.AddScoped<IGatewayWebhookProcessor, EfiPixWebhookProcessor>();

        // Signature validators (singleton — stateless, leem IConfiguration uma vez por call)
        services.AddSingleton<IWebhookSignatureValidator, EfiPixSignatureValidator>();

        // Efí Bank Pix Gateway
        var efiClientId = configuration["Efi:ClientId"];
        var efiUseStub = bool.TryParse(configuration["Efi:UseStub"], out var efiStub) && efiStub;
        if (efiUseStub)
        {
            // B0 — Stub Pix DEV-ONLY. Gate em código + hard-fail fora de Development: o stub gera
            // QR Pix falso que NUNCA reconcilia, então jamais pode rodar em produção.
            var envName = configuration["ASPNETCORE_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production";
            if (!string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Efi:UseStub=true só é permitido em Development (ambiente atual: {envName}). " +
                    "Remova a flag — StubEfiPixService gera QR Pix falso que não reconcilia (risco de receita perdida).");
            services.AddSingleton<IEfiPixService, StubEfiPixService>();
        }
        else if (!string.IsNullOrWhiteSpace(efiClientId))
        {
            var isSandboxStr = configuration["Efi:Sandbox"];
            var isSandbox = !bool.TryParse(isSandboxStr, out var sb) || sb;
            var baseUrl = isSandbox ? "https://pix-h.api.efipay.com.br" : "https://pix.api.efipay.com.br";
            services.AddTransient<IEfiPixService>(sp =>
            {
                var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                return new EfiPixService(
                    http,
                    sp.GetRequiredService<IMemoryCache>(),
                    configuration,
                    sp.GetRequiredService<ILogger<EfiPixService>>());
            });
        }
        else
        {
            services.AddSingleton<IEfiPixService, NoopEfiPixService>();
        }

        // Efí Bank Boleto Gateway (reutiliza mesmo ClientId e chave Sandbox que Pix, só muda baseUrl e endpoint v1)
        if (!string.IsNullOrWhiteSpace(efiClientId))
        {
            var isSandboxStr = configuration["Efi:Sandbox"];
            var isSandbox = !bool.TryParse(isSandboxStr, out var sb2) || sb2;
            var baseUrl = isSandbox ? "https://cobrancas-h.api.efipay.com.br" : "https://cobrancas.api.efipay.com.br";
            services.AddTransient<IEfiBoletoService>(sp =>
            {
                var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                return new EfiBoletoService(
                    http,
                    sp.GetRequiredService<IMemoryCache>(),
                    configuration,
                    sp.GetRequiredService<ILogger<EfiBoletoService>>());
            });
        }
        else
        {
            services.AddSingleton<IEfiBoletoService, NoopEfiBoletoService>();
        }

        // F12 — Adapters de gateways internacionais (stubs).
        // Registrados apenas quando ha credenciais OU quando AllowUnsigned=true
        // (DEV/sandbox). Em prod sem credenciais, permanecem fora do router para
        // nao oferecer metodos que vao falhar. Tambem envolvidos pelo decorator.
        if (!string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"])
            || string.Equals(configuration["Stripe:WebhookAllowUnsigned"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<StripeGatewayAdapter>();
            services.AddScoped<IPagamentoGateway>(sp => DecorateGateway(sp, sp.GetRequiredService<StripeGatewayAdapter>()));
            services.AddSingleton<IWebhookSignatureValidator, StripeSignatureValidator>();
        }
        if (!string.IsNullOrWhiteSpace(configuration["MercadoPago:AccessToken"])
            || string.Equals(configuration["MercadoPago:WebhookAllowUnsigned"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<MercadoPagoGatewayAdapter>();
            services.AddScoped<IPagamentoGateway>(sp => DecorateGateway(sp, sp.GetRequiredService<MercadoPagoGatewayAdapter>()));
            services.AddSingleton<IWebhookSignatureValidator, MercadoPagoSignatureValidator>();
        }

        return services;
    }

    private static MeasuredPagamentoGatewayDecorator DecorateGateway(IServiceProvider sp, IPagamentoGateway inner)
        => new(
            inner,
            sp.GetRequiredService<IGatewayHealthStore>(),
            sp.GetRequiredService<IGatewayErrorClassifier>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MeasuredPagamentoGatewayDecorator>>());
}

/// <summary>
/// Implementacao de email que apenas loga (fallback dev/test quando SMTP nao
/// configurado). O NOME desta classe e contrato: codigo de diagnostico checa
/// <c>GetType().Name == "ConsoleEmailService"</c> / <c>nameof(...)</c> para detectar
/// "SMTP nao configurado" — nao renomear sem atualizar esses call-sites.
/// </summary>
public sealed class ConsoleEmailService(ILogger<ConsoleEmailService> logger) : IEmailService
{
    // #288 item 4: era Console.WriteLine (sem nivel, sem estrutura). Agora ILogger em
    // Debug. NAO loga o corpo do email — e PII potencial (ver #301); so destinatario +
    // assunto, suficiente para o fallback de dev.
    public Task SendAsync(string to, string subject, string body, bool isHtml = false)
    {
        logger.LogDebug("[EMAIL] Para: {To} | Assunto: {Subject}", to, subject);
        return Task.CompletedTask;
    }

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
    {
        logger.LogDebug("[EMAIL] Para: {To} | Assunto: {Subject} | Anexos: {Count}", to, subject, attachments.Count());
        return Task.CompletedTask;
    }

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        logger.LogDebug("[EMAIL] Para: {Count} destinatario(s) | Assunto: {Subject}", to.Count(), subject);
        return Task.CompletedTask;
    }

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        logger.LogDebug("[EMAIL TEMPLATE] Para: {To} | Assunto: {Subject} | Template: {Template}", to, subject, templateName);
        return Task.CompletedTask;
    }
}