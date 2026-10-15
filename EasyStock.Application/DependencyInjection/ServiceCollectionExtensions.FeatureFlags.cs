// Feature flags por tenant (ADR-0048) — quais módulos cada empresa enxerga. Leitura pelo
// produto (tenant logado) e escrita pelo back-office (SuperAdmin).

using EasyStock.Application.UseCases.FeatureFlags;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases de feature flags por tenant.</summary>
    public static IServiceCollection AddEasyStockFeatureFlagUseCases(this IServiceCollection services)
    {
        // Produto (tenant logado)
        services.AddScoped<ObterFeaturesAtivasUseCase>();

        // Back-office (SuperAdmin)
        services.AddScoped<ListarFeaturesDoTenantUseCase>();
        services.AddScoped<DefinirFeatureDoTenantUseCase>();

        return services;
    }
}
