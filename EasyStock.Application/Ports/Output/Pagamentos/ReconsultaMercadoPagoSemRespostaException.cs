namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// O webhook avisou de um pagamento que o Mercado Pago diz não conhecer, e há cobrança online pendente
/// (I3.1, #1667; invariante 7). Propaga para o controller responder 5xx e o provedor reenviar: responder
/// 200 aqui deixaria um pedido pago sem baixa.
/// </summary>
public sealed class ReconsultaMercadoPagoSemRespostaException(string pagamentoId)
    : Exception($"Mercado Pago não reconheceu o pagamento {pagamentoId} na reconsulta e há cobrança pendente.");