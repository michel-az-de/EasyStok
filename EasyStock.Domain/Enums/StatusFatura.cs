namespace EasyStock.Domain.Enums;

/// <summary>
/// Estados do ciclo de vida de uma <see cref="Entities.Fatura"/>.
///
/// <para>
/// Transicoes validas:
/// </para>
/// <list type="bullet">
///   <item>Rascunho → Emitida (via EmitirFaturaUseCase quando admin/sistema confirma)</item>
///   <item>Emitida → ParcialmentePaga (registra primeiro pagamento &lt; total)</item>
///   <item>Emitida → Paga (registra pagamento que cobre total)</item>
///   <item>ParcialmentePaga → Paga (pagamento adicional cobre o saldo)</item>
///   <item>Emitida|ParcialmentePaga → Vencida (job marca apos DataVencimento)</item>
///   <item>Vencida → Paga|ParcialmentePaga (paga em atraso)</item>
///   <item>Rascunho|Emitida|ParcialmentePaga|Vencida → Cancelada (admin cancela)</item>
/// </list>
/// <para>
/// Paga e Cancelada sao terminais — nao reabrem (estorno gera novo evento, nao status).
/// </para>
/// </summary>
public enum StatusFatura
{
    Rascunho,
    Emitida,
    ParcialmentePaga,
    Paga,
    Vencida,
    Cancelada
}
