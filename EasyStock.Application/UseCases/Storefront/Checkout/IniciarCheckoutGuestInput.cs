namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Input do checkout GUEST storefront (issue #680).
///
/// Com janela e data (#1254): o guest reserva a vaga e é cobrado pelo Mercado Pago, como o
/// checkout logado. Sem prova OTP, usa um cadastro guest isolado e preserva o telefone
/// informado apenas como contato do pedido.
/// </summary>
/// <param name="Campanha">#2928: campanha do site; vale a última (D2).</param>
/// <param name="OrigemDaConversa">#2928: preenchida pelo <c>CheckoutSiteUseCase</c>; vale quando não veio campanha.</param>
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
    Guid? ConversaId = null,
    EasyStock.Application.Services.Pedidos.DadosEnderecoEntrega? EnderecoEntrega = null,
    EasyStock.Application.UseCases.Common.CampanhaInput? Campanha = null,
    EasyStock.Domain.ValueObjects.OrigemAnuncio? OrigemDaConversa = null);
