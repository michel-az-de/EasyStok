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
        services.AddScoped<EasyStock.Application.Services.Storefront.CheckoutCoreService>(); // S10: fases 1-2
        services.AddScoped<IniciarCheckoutUseCase>();

        // S11: cobrança do pedido pelo Mercado Pago (site e conversa), confirmação, expiração, troca de
        // forma e desfazer pagamento manual. ConfirmarPagamentoPedidoUseCase é o ponto de entrada da S32.
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.GerarCobrancaPedidoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.ConfirmarPagamentoPedidoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.AtualizarCobrancaPorPagamentoUseCase>(); // S32
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.TrocarFormaPagamentoPedidoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.DesfazerPagamentoManualUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Pedidos.Cobranca.ProcessarCobrancaVencidaUseCase>();
        services.AddScoped<EasyStock.Application.Services.Atendimento.AvisoCobrancaConversa>();

        // S20: canhoto e fila de impressão (o pedido pago entra na fila dentro do ConfirmarPagamento).
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.MontarCanhotoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.MontarPedidoImpressoUseCase>(); // S49: impresso do pedido
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.MontarComandaUseCase>(); // S52: comanda de cozinha
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.ListarImpressoesPendentesUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.ReimprimirCanhotoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.AlertarImpressoesAtrasadasUseCase>();
        // N11: aviso externo (e-mail e WhatsApp) da impressão travada, um escopo por impressão.
        services.AddScoped<EasyStock.Application.UseCases.Operacao.Impressao.NotificarImpressaoTravadaUseCase>();

        // S40: expediente da loja (abrir e fechar, horário por dia).
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Expediente.ObterExpedienteLojaUseCase>();
        // S45: cadastro de janelas, zonas e bloqueios pela própria loja
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Entrega.CadastroJanelasEntregaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Entrega.CadastroZonasFreteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Entrega.CadastroBloqueiosEntregaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Expediente.AtualizarExpedienteLojaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Expediente.DefinirControleExpedienteUseCase>();
        services.AddScoped<LiberarVagaOnPedidoCanceladoHandler>();

        // #680 — checkout GUEST sem login + token de acompanhamento.
        services.AddScoped<AcompanhamentoTokenService>();
        services.AddScoped<IniciarCheckoutGuestUseCase>();

        // TASK-EZ-APROVAR-001 — use cases Babá aprovar/recusar pedido.
        services.AddScoped<AprovarPedidoStorefrontUseCase>();
        services.AddScoped<RecusarPedidoStorefrontUseCase>();

        // TASK-EZ-PEDIDOS-001 — listagem do histórico de pedidos do cliente.
        services.AddScoped<ListarPedidosClienteUseCase>();

        // #670 — pedido individual do cliente (tela de acompanhamento).
        services.AddScoped<ObterPedidoClienteUseCase>();

        // #684 — pedido GUEST individual via token assinado (acompanhamento sem login).
        services.AddScoped<ObterPedidoGuestUseCase>();

        // TimeProvider: TimeProvider.System como singleton — entities e use cases
        // storefront usam injetado para testes determinísticos. AddSingleton ja
        // protege contra registro duplo se outro componente fizer o mesmo.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
