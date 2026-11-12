namespace EasyStock.Application.UseCases.CriarPedido;

/// <summary>
/// Valores de <c>Pedido.Origem</c> (string, até 20 caracteres, ver <see cref="CriarPedidoCommand.Origem"/>).
/// </summary>
public static class OrigemPedido
{
    /// <summary>Checkout logado do site (<c>IniciarCheckoutUseCase</c>).</summary>
    public const string Storefront = "storefront";

    /// <summary>Checkout sem login do site (<c>IniciarCheckoutGuestUseCase</c>, #680).</summary>
    public const string StorefrontGuest = "storefront-guest";

    /// <summary>Pedido fechado na conversa pelo agente do atendimento (S10).</summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>
    /// Origem do pedido fechado numa conversa: o chat do site vale como site (<see cref="Storefront"/>, o que
    /// "Meus pedidos" lista); os demais canais ficam com <see cref="WhatsApp"/>.
    /// </summary>
    public static string DoCanal(EasyStock.Domain.Enums.Atendimento.CanalConversa canal) =>
        canal == EasyStock.Domain.Enums.Atendimento.CanalConversa.ChatSite ? Storefront : WhatsApp;
}
