using System.Linq.Expressions;

namespace EasyStock.Domain.Sales;

/// <summary>
/// O que conta como compra do cliente (#2777): cancelado, rascunho e reserva ainda sem pagamento não são.
/// Uma regra só para o dossiê (Ficha), a lista de Cadastros, o filtro lead ou cliente e a ordenação.
/// </summary>
public static class RegraDeCompra
{
    public static bool EhCompra(string? status) =>
        status is not (StatusPedidoMapper.Cancelado or StatusPedidoMapper.Rascunho or StatusPedidoMapper.AguardandoPagamento);

    /// <summary>A mesma regra traduzível para SQL pelo EF.</summary>
    public static readonly Expression<Func<Pedido, bool>> Pedido = p =>
        p.Status != StatusPedidoMapper.Cancelado
        && p.Status != StatusPedidoMapper.Rascunho
        && p.Status != StatusPedidoMapper.AguardandoPagamento;
}
