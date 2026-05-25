namespace EasyStock.Domain.Enums;

/// <summary>
/// Status de uma <see cref="Entities.FaturaPagamento"/> (tentativa/registro de
/// pagamento). Diferente de <see cref="StatusFatura"/> — uma fatura pode ter
/// varios pagamentos em estados distintos (ex: 1 confirmado, 1 falhou, 1 estornado).
///
/// <para>
/// Transicoes:
/// </para>
/// <list type="bullet">
///   <item>Pendente → EmProcessamento (orchestrator iniciou attempt em algum gateway)</item>
///   <item>Pendente → Confirmado (webhook gateway confirma — pagamento manual em dinheiro)</item>
///   <item>EmProcessamento → Confirmado (webhook gateway confirma)</item>
///   <item>EmProcessamento → Falhou (todos gateways exauridos)</item>
///   <item>EmProcessamento → Cancelado (admin cancela)</item>
///   <item>Pendente → Falhou (legado: gateway recusa ou timeout)</item>
///   <item>Confirmado → EstornoSolicitado (admin solicita refund)</item>
///   <item>EstornoSolicitado → Estornado (gateway confirma refund)</item>
///   <item>EstornoSolicitado → Confirmado (refund recusado, volta ao estado anterior)</item>
/// </list>
///
/// <para>
/// Os valores <c>EmProcessamento</c> e <c>Cancelado</c> foram adicionados na
/// Onda P0 do Payment Orchestration. Pagamentos legados que estavam em
/// <c>Pendente</c> antes do orchestrator continuam validos — o orchestrator
/// novo cria com <c>EmProcessamento</c> direto.
/// </para>
/// </summary>
public enum StatusFaturaPagamento
{
    Pendente,
    Confirmado,
    EstornoSolicitado,
    Estornado,
    Falhou,
    EmProcessamento,
    Cancelado
}
