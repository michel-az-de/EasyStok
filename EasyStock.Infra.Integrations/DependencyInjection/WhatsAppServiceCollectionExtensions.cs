using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Infra.Integrations.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registro do provider de OTP via WhatsApp. Em Development usa o stub
/// (<see cref="StubWhatsAppOtpSender"/>) — provider real
/// (<c>WhatsAppCloudApiOtpSender</c>) será adicionado em TASK-EZ-WA-001
/// após Meta Business Verification (TASK-HUM-001).
/// </summary>
public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="StubWhatsAppOtpSender"/> como <see cref="IWhatsAppOtpSender"/>.
    /// O stub NUNCA roda em Production (guarda no construtor — defense-in-depth).
    /// Caller (<c>Program.cs</c>) é responsável por chamar isto apenas quando
    /// fizer sentido (Development ou flag de config).
    /// </summary>
    public static IServiceCollection AddEasyStockWhatsAppStub(this IServiceCollection services)
    {
        services.AddScoped<IWhatsAppOtpSender, StubWhatsAppOtpSender>();
        return services;
    }
}
