namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Input do checkout GUEST storefront (issue #680).
///
/// Com janela e data (#1254): o guest reserva a vaga e é cobrado pelo Mercado Pago, como o
/// checkout logado. Sem prova OTP, usa um cadastro guest isolado e preserva o telefone
/// informado apenas como contato do pedido.
/// </summary>
public sealed record IniciarCheckoutGuestInput(
    string Slug,
    string Nome,
    string Telefone,
    string Cep,
    string? Numero,
    IReadOnlyList<CheckoutItemInput> Items,
    Guid? JanelaId,
    DateOnly? DataEntrega,
    string? Observacoes = null,
    Guid? ConversaId = null);
