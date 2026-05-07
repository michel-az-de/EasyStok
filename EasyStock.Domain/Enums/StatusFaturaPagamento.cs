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
///   <item>Pendente → Confirmado (webhook gateway confirma)</item>
///   <item>Pendente → Falhou (gateway recusa ou timeout)</item>
///   <item>Confirmado → EstornoSolicitado (admin solicita refund)</item>
///   <item>EstornoSolicitado → Estornado (gateway confirma refund)</item>
///   <item>EstornoSolicitado → Confirmado (refund recusado, volta ao estado anterior)</item>
/// </list>
/// </summary>
public enum StatusFaturaPagamento
{
    Pendente,
    Confirmado,
    EstornoSolicitado,
    Estornado,
    Falhou
}
