namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>
/// O gateway recusou o estorno automático de um pagamento que chegou depois do cancelamento do pedido.
/// Propaga até o controller do webhook, que responde 500: o Mercado Pago reenvia a notificação e o estorno
/// é tentado de novo com a mesma chave de idempotência.
/// </summary>
public sealed class EstornoAutomaticoFalhouException(string pagamentoExternoId, string? erro)
    : Exception($"Estorno automático do pagamento {pagamentoExternoId} recusado: {erro ?? "sem_detalhe"}.")
{
    public string PagamentoExternoId { get; } = pagamentoExternoId;
}
