namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Resultado do checkout GUEST (issue #680).
///
/// <para><c>NumeroCurto</c>: 8 chars iniciais do Guid em uppercase. Identificador
/// humano-legivel pra mensagem WhatsApp ("Pedido #A1B2C3D4"). Backend nao
/// mantém sequencial — o ID Guid e a chave canonica.</para>
///
/// <para><c>AcompanhamentoToken</c>: JWT HS256 TTL 30d, scope "acomp". Permite
/// que o guest acompanhe o pedido em <c>/pedido-status.html?id=...&amp;t=...</c>
/// sem login (issue #681).</para>
///
/// <para><c>FreteEstimado</c>: opcional. Quando o CEP cobre uma FreteZona ativa,
/// devolve o valor pra UI exibir. Quando nao cobre, devolve null e Babá
/// negocia frete via WhatsApp.</para>
/// </summary>
public sealed record IniciarCheckoutGuestResult(
    Guid PedidoId,
    string NumeroCurto,
    string AcompanhamentoToken,
    decimal? FreteEstimado);
