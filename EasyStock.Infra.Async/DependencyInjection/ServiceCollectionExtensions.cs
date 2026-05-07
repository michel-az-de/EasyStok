using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Pagamentos;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.Infra.Async.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Email Service
        var smtpConfig = configuration.GetSection("Smtp");
        if (smtpConfig.Exists())
        {
            services.AddSingleton<IEmailService>(sp => new SmtpEmailService(
                smtpConfig["Host"] ?? "localhost",
                int.Parse(smtpConfig["Port"] ?? "587"),
                smtpConfig["Username"] ?? "",
                smtpConfig["Password"] ?? "",
                smtpConfig["FromEmail"] ?? "noreply@easystock.com",
                smtpConfig["FromName"] ?? "EasyStock",
                bool.Parse(smtpConfig["EnableSsl"] ?? "true")));
        }
        else
        {
            // Fallback para desenvolvimento
            services.AddSingleton<IEmailService, ConsoleEmailService>();
        }

        // Storage Service
        services.AddSingleton<IStorageService, S3StorageService>();

        // PDF Renderer (Modulo Financeiro F4) — QuestPDF stateless + threadsafe
        services.AddSingleton<IFaturaPdfRenderer, FaturaPdfRenderer>();

        // Modulo Financeiro F3 — abstracao multi-gateway de pagamento.
        // Os adapters precisam ser Scoped (consomem repos scoped via DI).
        services.AddScoped<IPagamentoGateway, EfiPixGatewayAdapter>();
        services.AddScoped<IPagamentoGateway, ManualGatewayAdapter>();
        services.AddScoped<IPagamentoGatewayRouter, PagamentoGatewayRouter>();

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
                var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(baseUrl) };
                return new EfiPixService(
                    http,
                    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                    configuration,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EfiPixService>>());
            });
        }
        else
        {
            services.AddSingleton<IEfiPixService, NoopEfiPixService>();
        }

        // Efí Bank Boleto Gateway (reutiliza mesmo baseUrl que Pix, só muda endpoint v1)
        var efiClientIdBoleto = configuration["Efi:ClientId"];
        if (!string.IsNullOrWhiteSpace(efiClientIdBoleto))
        {
            var isSandboxStr = configuration["Efi:Sandbox"];
            var isSandbox = !bool.TryParse(isSandboxStr, out var sb2) || sb2;
            var baseUrl = isSandbox ? "https://cobrancas-h.api.efipay.com.br" : "https://cobrancas.api.efipay.com.br";
            services.AddTransient<IEfiBoletoService>(sp =>
            {
                var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(baseUrl) };
                return new EfiBoletoService(
                    http,
                    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                    configuration,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EfiBoletoService>>());
            });
        }
        else
        {
            services.AddSingleton<IEfiBoletoService, NoopEfiBoletoService>();
        }

        // F12 — Adapters de gateways internacionais (stubs).
        // Registrados apenas quando ha credenciais OU quando AllowUnsigned=true
        // (DEV/sandbox). Em prod sem credenciais, permanecem fora do router para
        // nao oferecer metodos que vao falhar.
        if (!string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"])
            || string.Equals(configuration["Stripe:WebhookAllowUnsigned"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IPagamentoGateway, StripeGatewayAdapter>();
            services.AddSingleton<IWebhookSignatureValidator, StripeSignatureValidator>();
        }
        if (!string.IsNullOrWhiteSpace(configuration["MercadoPago:AccessToken"])
            || string.Equals(configuration["MercadoPago:WebhookAllowUnsigned"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IPagamentoGateway, MercadoPagoGatewayAdapter>();
            services.AddSingleton<IWebhookSignatureValidator, MercadoPagoSignatureValidator>();
        }

        return services;
    }
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