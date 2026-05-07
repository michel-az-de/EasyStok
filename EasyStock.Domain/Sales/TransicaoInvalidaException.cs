namespace EasyStock.Domain.Sales;

/// <summary>
/// Lançada quando um <see cref="Entities.Pedido"/> tenta sair de um estado
/// para outro mas a transição não é permitida pela
/// <see cref="PedidoStateMachine"/>.
///
/// <para>
/// Convertida pela camada Application em
/// <see cref="UseCases.Common.UseCaseValidationException"/> (HTTP 400)
/// quando emerge dos use cases.
/// </para>
/// </summary>
public sealed class TransicaoInvalidaException : Exception
{
    public StatusPedido De { get; }
    public StatusPedido Para { get; }

    public TransicaoInvalidaException(StatusPedido de, StatusPedido para)
        : base($"Transição inválida: {StatusPedidoMapper.Format(de)} → {StatusPedidoMapper.Format(para)}.")
    {
        De = de;
        Para = para;
    }
}
