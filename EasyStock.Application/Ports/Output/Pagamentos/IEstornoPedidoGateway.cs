namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>Resultado do estorno de um pagamento de pedido (S27).</summary>
/// <param name="Sucesso">O gateway aceitou o estorno.</param>
/// <param name="IdSolicitacao">Id do estorno no gateway, quando aceito.</param>
/// <param name="Erro">Código ou mensagem do gateway, quando recusado.</param>
/// <param name="Desfecho">O que se sabe do estorno (I3.1). Só <see cref="DesfechoEstornoPedido.Recusado"/> é recusa.</param>
public sealed record EstornoPedidoResult(bool Sucesso, string? IdSolicitacao, string? Erro, DesfechoEstornoPedido Desfecho)
{
    public static EstornoPedidoResult Ok(string idSolicitacao) => new(true, idSolicitacao, null, DesfechoEstornoPedido.Aprovado);
    public static EstornoPedidoResult Falha(string erro) => new(false, null, erro, DesfechoEstornoPedido.Recusado);

    /// <summary>Sem resposta conclusiva (timeout, 5xx, estorno em análise): pode ter sido efetivado.</summary>
    public static EstornoPedidoResult Indeterminado(string erro) => new(false, null, erro, DesfechoEstornoPedido.Indeterminado);

    /// <summary>Estorno feito pelo simulador: o fluxo segue, mas nenhum dinheiro voltou.</summary>
    public static EstornoPedidoResult Simulado(string idSolicitacao) => new(true, idSolicitacao, null, DesfechoEstornoPedido.Simulado);
}

/// <summary>Desfecho de um pedido de estorno (I3.1, #1667; invariantes 2 e 13).</summary>
public enum DesfechoEstornoPedido
{
    Aprovado,
    Recusado,
    Indeterminado,
    Simulado,
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
