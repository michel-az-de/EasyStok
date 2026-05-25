using EasyStock.Application.UseCases.Storefront.Agendamento;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases públicos do Storefront (agendamento, cardápio, frete, etc.).
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        services.AddScoped<ListarJanelasDisponiveisUseCase>();
        return services;
    }
}
