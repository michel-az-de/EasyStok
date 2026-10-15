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
/// <para><c>FreteEstimado</c>: frete da zona do CEP, já somado à cobrança.</para>
///
/// <para><c>LinkPagamento</c>: <c>init_point</c> do Checkout Pro (Pix ou cartão), válido por
/// <c>ExpiresIn</c> segundos (#1254). O site redireciona para ele.</para>
/// </summary>
public sealed record IniciarCheckoutGuestResult(
    Guid PedidoId,
    string NumeroCurto,
    string AcompanhamentoToken,
    decimal? FreteEstimado,
    string? LinkPagamento = null,
    int ExpiresIn = 0);
