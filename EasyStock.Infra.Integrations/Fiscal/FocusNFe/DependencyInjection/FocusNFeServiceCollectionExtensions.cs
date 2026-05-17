using EasyStock.Application.Ports.Output.Fiscal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe.DependencyInjection;

/// <summary>
/// Registra o adapter Focus NFe e suas dependencias. Deve ser chamado depois de
/// <c>AddEasyStockIntegrationResilience()</c> (que registra os pipelines Polly).
///
/// <para>
/// Configure <c>FocusNFe</c> no appsettings.json:
/// <code>
/// "FocusNFe": {
///   "BaseUrl": "https://homologacao.focusnfe.com.br/v2/",
///   "TimeoutSeconds": 30,
///   "WebhookSecret": "&lt;configurar via env var FocusNFe__WebhookSecret&gt;"
/// }
/// </code>
/// </para>
/// </summary>
public static class FocusNFeServiceCollectionExtensions
{
    public static IServiceCollection AddFocusNFeAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FocusNFeOptions>(configuration.GetSection(FocusNFeOptions.SectionName));

        services.AddHttpClient<FocusNFeHttpClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FocusNFeOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddScoped<FocusNFeWebhookValidator>();
        services.AddScoped<INfeCertificadoA1Service, NfeCertificadoA1Service>();
        services.AddScoped<IGatewayFiscal, FocusNFeAdapter>();

        return services;
    }
}
