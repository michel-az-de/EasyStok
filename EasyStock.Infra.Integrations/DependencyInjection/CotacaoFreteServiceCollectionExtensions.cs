using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Infra.Integrations.Logistica.Lalamove;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Frete pela cotação externa (#1882): a Lalamove cota com a chave de produção da loja. Sem retry e com
/// teto curto, porque o cliente espera o frete no checkout; falha volta à tabela da loja.
/// Depende do <c>IIntegrationCredentialResolver</c> (Postgres), então só entra com banco.
/// </summary>
public static class CotacaoFreteServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockCotacaoFrete(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<ICotacaoFreteExterna, CotadorFreteLalamove>(c =>
        {
            c.BaseAddress = new Uri(CotadorFreteLalamove.HostProducao);
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
