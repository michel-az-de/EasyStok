using System.Linq.Expressions;

namespace EasyStock.Domain.Sales;

/// <summary>
/// Pedido atrasado (#3294): marcado para uma hora que já passou e ainda não entregue, cancelado nem rascunho.
/// A mesma leitura do "atrasado" da urgência (<c>PedidoOrdering.PorUrgencia</c>). Pedido para agora (sem
/// <c>AgendadoParaEm</c>) nunca fica atrasado nesta regra.
/// </summary>
public static class RegraDeAtraso
{
    public static bool EstaAtrasado(string? status, DateTime? agendadoParaEm, DateTime agoraUtc) =>
        agendadoParaEm is { } marcado && marcado < agoraUtc
        && status is not (StatusPedidoMapper.Entregue or StatusPedidoMapper.Cancelado or StatusPedidoMapper.Rascunho);

    /// <summary>A mesma regra traduzível para SQL pelo EF.</summary>
    public static Expression<Func<Pedido, bool>> Pedido(DateTime agoraUtc) => p =>
        p.AgendadoParaEm != null && p.AgendadoParaEm < agoraUtc
        && p.Status != StatusPedidoMapper.Entregue
        && p.Status != StatusPedidoMapper.Cancelado
        && p.Status != StatusPedidoMapper.Rascunho;
}
