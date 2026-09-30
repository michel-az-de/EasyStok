namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>Resultado do estorno de um pagamento de pedido (S27).</summary>
/// <param name="Sucesso">O gateway aceitou o estorno.</param>
/// <param name="IdSolicitacao">Id do estorno no gateway, quando aceito.</param>
/// <param name="Erro">Código ou mensagem do gateway, quando recusado.</param>
public sealed record EstornoPedidoResult(bool Sucesso, string? IdSolicitacao, string? Erro)
{
    public static EstornoPedidoResult Ok(string idSolicitacao) => new(true, idSolicitacao, null);
    public static EstornoPedidoResult Falha(string erro) => new(false, null, erro);
}

/// <summary>
/// Estorno de pagamento de pedido no gateway (S27). No Mercado Pago é
/// <c>POST v1/payments/{id}/refunds</c> com <c>X-Idempotency-Key</c> (S32): repetir a mesma chave não
/// estorna duas vezes. Separada de <see cref="IMercadoPagoClient"/> para o use case não depender do
/// cliente HTTP inteiro.
/// </summary>
public interface IEstornoPedidoGateway
{
    Task<EstornoPedidoResult> EstornarAsync(
        string pagamentoExternoId, decimal valor, string chaveIdempotencia, CancellationToken ct = default);
}
