using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.Fiscal.DependencyInjection;

/// <summary>
/// Registra o adapter Focus NFe e dependencias relacionadas. O pipeline
/// Polly da categoria "fiscal" é registrado por
/// <c>AddEasyStockIntegrationResilience</c> — chame ANTES desta extension.
/// </summary>
public static class FiscalIntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockFiscalIntegrations(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FocusNFeOptions>().Bind(configuration.GetSection("FocusNFe"));

        services.AddHttpClient<FocusNFeHttpClient>();

        services.AddScoped<FocusNFePayloadMapper>();
        services.AddScoped<FocusNFeResponseMapper>();
        services.AddScoped<FocusNFeWebhookValidator>();

        services.AddScoped<IGatewayFiscal, FocusNFeAdapter>();
        services.AddScoped<INotaFiscalCertificadoA1Service, NotaFiscalCertificadoA1Service>();

        return services;
    }
}
