namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>
/// Operação de cobrança recusada pelo estado do pedido (409). <see cref="Codigo"/> é o contrato de fio:
/// <c>pedido_ja_pago</c>, <c>use_estorno</c>, <c>preparo_iniciado</c>, <c>pedido_finalizado</c>,
/// <c>sem_pagamento</c>, <c>forma_na_entrega</c>.
/// </summary>
public sealed class CobrancaPedidoConflitoException(string codigo, string mensagem) : Exception(mensagem)
{
    public const string PedidoJaPago = "pedido_ja_pago";
    public const string UseEstorno = "use_estorno";
    public const string PreparoIniciado = "preparo_iniciado";
    public const string PedidoFinalizado = "pedido_finalizado";
    public const string SemPagamento = "sem_pagamento";
    public const string FormaNaEntrega = "forma_na_entrega";

    public string Codigo { get; } = codigo;
}

/// <summary>Pedido inexistente ou de outro tenant (404, sem oráculo de existência).</summary>
public sealed class CobrancaPedidoNaoEncontradoException(Guid pedidoId)
    : Exception($"Pedido {pedidoId} não encontrado.")
{
    public Guid PedidoId { get; } = pedidoId;
}
