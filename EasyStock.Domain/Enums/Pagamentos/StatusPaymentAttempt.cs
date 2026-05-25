namespace EasyStock.Domain.Enums.Pagamentos;

/// <summary>
/// Status de uma <see cref="Entities.Pagamentos.PaymentAttempt"/> — uma
/// tentativa atomica de cobranca em UM gateway. Pode haver N attempts por
/// <see cref="Entities.FaturaPagamento"/> (fallback A→B, retries no mesmo
/// gateway).
///
/// <para>
/// Transicoes validas:
/// </para>
/// <list type="bullet">
///   <item><c>Iniciado</c> → <c>Sucesso</c> (webhook ou ConsultarAsync confirma)</item>
///   <item><c>Iniciado</c> → <c>FalhaRetentavel</c> (5xx, network, timeout — retry mesmo gateway)</item>
///   <item><c>Iniciado</c> → <c>FalhaPermanente</c> (4xx semantico nao-recuperavel — fallback gateway)</item>
///   <item><c>Iniciado</c> → <c>Recusado</c> (Declined: card_declined, insufficient_funds — fallback gateway se cartao)</item>
///   <item><c>Iniciado</c> → <c>CircuitOpen</c> (Polly circuit aberto — pula gateway, fallback)</item>
///   <item><c>Iniciado</c> → <c>Cancelado</c> (admin cancela explicitamente)</item>
///   <item><c>FalhaRetentavel</c> → <c>Sucesso</c> (reconciliador converge)</item>
///   <item><c>FalhaRetentavel</c> → <c>FalhaPermanente</c> (esgotou tentativas)</item>
///   <item><c>Iniciado</c> → <c>Inconclusivo</c> (ConsultarAsync devolve estado desconhecido)</item>
/// </list>
///
/// <para>
/// Estados terminais: <c>Sucesso</c>, <c>FalhaPermanente</c>, <c>Recusado</c>,
/// <c>Cancelado</c>. <c>FalhaRetentavel</c>, <c>CircuitOpen</c>, <c>Inconclusivo</c>
/// sao intermediarios — reconciliador (P1) converge para terminal.
/// </para>
///
/// <para>
/// Invariante critica: dado um <c>FaturaPagamentoId</c>, no maximo UM attempt
/// pode ter <c>Status == Sucesso</c> (Postgres partial unique index garante).
/// </para>
/// </summary>
public enum StatusPaymentAttempt
{
    Iniciado = 1,
    Sucesso = 2,
    FalhaRetentavel = 3,
    FalhaPermanente = 4,
    Recusado = 5,
    CircuitOpen = 6,
    Cancelado = 7,
    Inconclusivo = 8
}
