namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>Input do checkout storefront (ADR-0014).</summary>
public sealed record IniciarCheckoutInput(
    string Slug,
    Guid ClienteId,
    IReadOnlyList<CheckoutItemInput> Items,
    Guid JanelaId,
    DateOnly DataEntrega,
    string Cep,
    string? Observacoes = null,
    Guid? IdempotencyKey = null,
    string? ContentHash = null);

public sealed record CheckoutItemInput(Guid CardapioItemId, int Qtd);
