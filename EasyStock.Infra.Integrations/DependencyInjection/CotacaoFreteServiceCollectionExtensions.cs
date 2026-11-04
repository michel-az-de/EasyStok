using EasyStock.Application.Ports.Output.Integration.Logistica;
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

    /// <summary>
    /// Despacho da corrida pela API da Lalamove (#2160, ADR-0064): um cliente por ambiente, sem retry (R3), porque o
    /// POST de criação não tem idempotência. O ambiente vem da conexão da loja.
    /// </summary>
    public static IServiceCollection AddEasyStockDespachoLalamove(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(LalamoveLogisticaGateway.ClienteProducao, c =>
        {
            c.BaseAddress = new Uri(CotadorFreteLalamove.HostProducao);
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient(LalamoveLogisticaGateway.ClienteSandbox, c =>
        {
            c.BaseAddress = new Uri(Conexao.TestadorLalamove.HostSandbox);
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<ILogisticaGateway, LalamoveLogisticaGateway>();
        return services;
    }
}
