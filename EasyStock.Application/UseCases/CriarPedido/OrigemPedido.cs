namespace EasyStock.Application.UseCases.CriarPedido;

/// <summary>
/// Valores de <c>Pedido.Origem</c> (string, até 20 caracteres, ver <see cref="CriarPedidoCommand.Origem"/>).
/// </summary>
public static class OrigemPedido
{
    /// <summary>Checkout logado do site (<c>IniciarCheckoutUseCase</c>).</summary>
    public const string Storefront = "storefront";

    /// <summary>Pedido fechado na conversa pelo agente do atendimento (S10).</summary>
    public const string WhatsApp = "whatsapp";
}
