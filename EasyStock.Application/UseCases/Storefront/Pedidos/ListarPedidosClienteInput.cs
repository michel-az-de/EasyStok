namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>Input do <see cref="ListarPedidosClienteUseCase"/>.</summary>
/// <param name="Slug">Slug do storefront (rota).</param>
/// <param name="ClienteId">ID do cliente — derivado do cookie de sessão <c>__Host-cdb_session</c> pelo controller.</param>
/// <param name="Limit">Quantidade máxima. Caller passa null/valor inválido → default 20. Clamp pra [1, 50] dentro do use case.</param>
public sealed record ListarPedidosClienteInput(
    string Slug,
    Guid ClienteId,
    int? Limit = null);

/// <summary>Wrapper do resultado — array de pedidos no shape do contrato.</summary>
public sealed record ListarPedidosClienteResult(
    IReadOnlyList<PedidoStorefrontDto> Pedidos);
