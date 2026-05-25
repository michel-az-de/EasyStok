using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Application.UseCases.Storefront.Webhook;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases públicos do Storefront (agendamento, cardápio, frete, checkout,
    /// webhook MP, etc.).
    /// </summary>
    public static IServiceCollection AddEasyStockStorefrontUseCases(this IServiceCollection services)
    {
        services.AddScoped<CheckoutIdempotencyService>();
        services.AddScoped<ListarJanelasDisponiveisUseCase>();
        services.AddScoped<IniciarCheckoutUseCase>();
        services.AddScoped<LiberarVagaOnPedidoCanceladoHandler>();

        // Webhook MP (ADR-0006 — receive-then-process)
        services.AddSingleton<MpHmacValidator>();
        services.AddScoped<ReceberWebhookMpUseCase>();
        services.AddScoped<ProcessarWebhookMpUseCase>();
        return services;
    }
}
