namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>Input do checkout storefront (ADR-0014).</summary>
/// <param name="Campanha">#2928: campanha do site; vale a última (D2).</param>
/// <param name="OrigemDaConversa">#2928: preenchida pelo <c>CheckoutSiteUseCase</c>; vale quando não veio campanha.</param>
public sealed record IniciarCheckoutInput(
    string Slug,
    Guid ClienteId,
    IReadOnlyList<CheckoutItemInput> Items,
    Guid JanelaId,
    DateOnly DataEntrega,
    string Cep,
    string? Observacoes = null,
    Guid? IdempotencyKey = null,
    string? ContentHash = null,
    string? Numero = null,
    Guid? ConversaId = null,
    EasyStock.Application.Services.Pedidos.DadosEnderecoEntrega? EnderecoEntrega = null,
    EasyStock.Application.UseCases.Common.CampanhaInput? Campanha = null,
    EasyStock.Domain.ValueObjects.OrigemAnuncio? OrigemDaConversa = null);

public sealed record CheckoutItemInput(Guid CardapioItemId, int Qtd);
