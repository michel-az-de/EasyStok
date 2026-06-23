namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>Input do <see cref="ObterPedidoClienteUseCase"/>.</summary>
/// <param name="Slug">Slug do storefront (rota).</param>
/// <param name="ClienteId">ID do cliente — derivado do cookie de sessão <c>__Host-cdb_session</c> pelo controller.</param>
/// <param name="PedidoId">ID do pedido a obter.</param>
public sealed record ObterPedidoClienteInput(
    string Slug,
    Guid ClienteId,
    Guid PedidoId);

/// <summary>
/// Wrapper do resultado — UM pedido no shape do contrato. Serializa como
/// <c>{ "pedido": {...} }</c> (consumido por <c>casa-da-baba/pedido-status.html</c>).
/// </summary>
public sealed record ObterPedidoClienteResult(
    PedidoStorefrontDto Pedido);
