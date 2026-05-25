// Camada Storefront — use cases publicos consumidos pela storefront
// (Casa da Baba e tenants futuros). Inclui: autenticacao OTP, agendamento,
// menu, frete, checkout, avaliacao.
//
// Use cases sao stateless e por requisicao — registro Scoped (padrao do projeto).

using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Application.UseCases.Storefront.Frete;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do storefront (autenticacao, agendamento, menu, etc.).
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        // Autenticacao via OTP (EZ-AUTH-001, EZ-AUTH-002)
        services.AddScoped<SolicitarOtpUseCase>();

        // Frete (EZ-FRETE-001)
        services.AddScoped<CalcularFreteUseCase>();

        // TimeProvider: TimeProvider.System como singleton — entities e use cases
        // storefront usam injetado para testes determinísticos. AddSingleton ja
        // protege contra registro duplo se outro componente fizer o mesmo.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
