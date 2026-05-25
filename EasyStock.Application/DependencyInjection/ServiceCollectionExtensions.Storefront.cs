using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases públicos do Storefront (agendamento, cardápio, frete, checkout, etc.).
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        services.AddScoped<CheckoutIdempotencyService>();
        services.AddScoped<ListarJanelasDisponiveisUseCase>();
        services.AddScoped<IniciarCheckoutUseCase>();
        services.AddScoped<LiberarVagaOnPedidoCanceladoHandler>();
        return services;
    }
}
