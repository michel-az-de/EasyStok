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
        // Cache Service
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
        if (!string.IsNullOrWhiteSpace(efiClientId))
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
/// Implementacao de email que apenas loga no console.
/// Util para desenvolvimento e testes.
/// </summary>
public sealed class ConsoleEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body, bool isHtml = false)
    {
        Console.WriteLine($"[EMAIL] To: {to}, Subject: {subject}, Body: {body}");
        return Task.CompletedTask;
    }

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
    {
        Console.WriteLine($"[EMAIL] To: {to}, Subject: {subject}, Body: {body}, Attachments: {attachments.Count()}");
        return Task.CompletedTask;
    }

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        Console.WriteLine($"[EMAIL] To: {string.Join(", ", to)}, Subject: {subject}, Body: {body}");
        return Task.CompletedTask;
    }

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        Console.WriteLine($"[EMAIL TEMPLATE] To: {to}, Subject: {subject}, Template: {templateName}");
        return Task.CompletedTask;
    }
}