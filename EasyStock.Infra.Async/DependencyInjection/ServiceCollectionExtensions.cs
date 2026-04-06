using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async;
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

        return services;
    }
}

/// <summary>
/// Implementação de email que apenas loga no console.
/// Útil para desenvolvimento e testes.
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