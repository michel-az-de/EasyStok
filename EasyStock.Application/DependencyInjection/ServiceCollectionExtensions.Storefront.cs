// Camada Storefront — use cases publicos consumidos pela storefront
// (Casa da Baba e tenants futuros). Inclui: autenticacao OTP, agendamento,
// menu, frete, checkout, avaliacao, aprovacao, pedidos.
//
// Use cases sao stateless e por requisicao — registro Scoped (padrao do projeto).

using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Application.UseCases.Storefront.Pedidos;
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
        services.AddScoped<ValidarOtpUseCase>();

        // Frete (EZ-FRETE-001)
        services.AddScoped<CalcularFreteUseCase>();

        // Menu / cardapio publico (EZ-MENU-001)
        services.AddScoped<ListarCardapioPublicoUseCase>();

        // Agendamento de entrega (EZ-AGEND-001)
        services.AddScoped<ListarJanelasDisponiveisUseCase>();

        // Checkout (CHECKOUT-001 base + WEBHOOK-001)
        services.AddScoped<CheckoutIdempotencyService>();
        services.AddScoped<IniciarCheckoutUseCase>();
        services.AddScoped<LiberarVagaOnPedidoCanceladoHandler>();

        // TASK-EZ-APROVAR-001 — use cases Babá aprovar/recusar pedido.
        services.AddScoped<AprovarPedidoStorefrontUseCase>();
        services.AddScoped<RecusarPedidoStorefrontUseCase>();

        // TASK-EZ-PEDIDOS-001 — listagem do histórico de pedidos do cliente.
        services.AddScoped<ListarPedidosClienteUseCase>();

        // #670 — pedido individual do cliente (tela de acompanhamento).
        services.AddScoped<ObterPedidoClienteUseCase>();

        // TimeProvider: TimeProvider.System como singleton — entities e use cases
        // storefront usam injetado para testes determinísticos. AddSingleton ja
        // protege contra registro duplo se outro componente fizer o mesmo.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
