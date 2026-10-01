namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Input do checkout GUEST storefront (issue #680).
///
/// Com janela e data (#1254): o guest reserva a vaga e é cobrado pelo Mercado Pago, como o
/// checkout logado. Sem ClienteId: use case resolve por <c>telefoneHash</c> (cria Cliente novo
/// na empresa se for guest novo; reusa se telefone já existir).
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
    string? Observacoes = null);
