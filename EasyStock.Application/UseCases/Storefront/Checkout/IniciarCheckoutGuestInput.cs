namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Input do checkout GUEST storefront (issue #680).
///
/// Sem JanelaId/DataEntrega: pedido nasce em <c>aguardando_aprovacao_baba</c>
/// e Babá agenda manualmente via WhatsApp depois. Sem ClienteId: use case
/// resolve por <c>telefoneHash</c> (cria Cliente novo na empresa se for guest
/// novo; reusa se telefone já existir).
/// </summary>
public sealed record IniciarCheckoutGuestInput(
    string Slug,
    string Nome,
    string Telefone,
    string Cep,
    string? Numero,
    IReadOnlyList<CheckoutItemInput> Items,
    string? Observacoes = null);
