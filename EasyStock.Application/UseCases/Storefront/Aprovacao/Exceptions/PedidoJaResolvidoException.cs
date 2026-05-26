using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;

/// <summary>
/// Lançada quando um pedido já foi aprovado/recusado/cancelado por outro agente Babá
/// (ou pelo background service de timeout) entre o lookup e o lock pessimista.
/// Mapeada para HTTP 409 pelo <c>AprovacaoPedidoController</c>.
///
/// <para>
/// Contém o <see cref="StatusAtual"/> e o <see cref="ResolvidoEm"/> para que o frontend
/// possa exibir mensagem precisa ("já foi aprovado em X por Y").
/// </para>
/// </summary>
public sealed class PedidoJaResolvidoException : Exception
{
    public Guid PedidoId { get; }
    public StatusPedido StatusAtual { get; }
    public string StatusAtualString { get; }
    public DateTime? ResolvidoEm { get; }

    public PedidoJaResolvidoException(
        Guid pedidoId,
        StatusPedido statusAtual,
        DateTime? resolvidoEm,
        string? mensagemExtra = null)
        : base(BuildMensagem(pedidoId, statusAtual, resolvidoEm, mensagemExtra))
    {
        PedidoId = pedidoId;
        StatusAtual = statusAtual;
        StatusAtualString = StatusPedidoMapper.Format(statusAtual);
        ResolvidoEm = resolvidoEm;
    }

    private static string BuildMensagem(
        Guid pedidoId, StatusPedido statusAtual, DateTime? resolvidoEm, string? extra)
    {
        var quando = resolvidoEm.HasValue
            ? $" em {resolvidoEm.Value:yyyy-MM-ddTHH:mm:ssZ}"
            : string.Empty;
        var status = StatusPedidoMapper.Format(statusAtual);
        var basico = $"Pedido {pedidoId} já está em status '{status}'{quando}.";
        return string.IsNullOrWhiteSpace(extra) ? basico : $"{basico} {extra}";
    }
}
