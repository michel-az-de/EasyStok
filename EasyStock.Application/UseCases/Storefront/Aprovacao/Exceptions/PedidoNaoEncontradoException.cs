namespace EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;

/// <summary>
/// Lançada quando o pedido não existe OU pertence a outro tenant (EmpresaId diferente).
/// Mapeada para HTTP 404 pelo controller — usar 404 em vez de 403 evita oracle
/// de existência cross-tenant (não vaza "esse pedido existe, mas você não pode ver").
/// </summary>
public sealed class PedidoNaoEncontradoException(Guid pedidoId)
    : Exception($"Pedido {pedidoId} não encontrado.")
{
    public Guid PedidoId { get; } = pedidoId;
}
