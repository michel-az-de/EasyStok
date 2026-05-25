// Camada Public — UseCases anonimos servidos pela landing publica
// (captura de leads, newsletter, fale-conosco). Sem multi-tenant.

using EasyStock.Application.UseCases.Public;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases publicos da landing — anonimos, sem EmpresaId.
    /// </summary>
    public static IServiceCollection AddEasyStockPublicUseCases(this IServiceCollection services)
    {
        services.AddScoped<RegistrarLeadPublicoUseCase>();
        return services;
    }
}
