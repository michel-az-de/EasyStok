using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Repositorio de <see cref="PaymentAttempt"/>. Multi-tenant strict — todas
/// as queries exigem <c>empresaId</c>.
///
/// <para>
/// <b>Audit trail</b>: ao adicionar attempt, repository tambem persiste o
/// <see cref="PaymentAttemptEvent"/> de criacao na MESMA transacao (via
/// <c>IUnitOfWork</c>).
/// </para>
/// </summary>
public interface IPaymentAttemptRepository
{
    /// <summary>Adiciona attempt + evento inicial em mesma transacao.</summary>
    Task AdicionarAsync(PaymentAttempt attempt, string motivoEvento, CancellationToken ct = default);

    Task<PaymentAttempt?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentAttempt>> ListarPorFaturaPagamentoAsync(
        Guid empresaId, Guid faturaPagamentoId, CancellationToken ct = default);

    /// <summary>
    /// Conta attempts em estado <see cref="StatusPaymentAttempt.FalhaPermanente"/>
    /// para uma fatura — usado pelo trigger F14 (helpdesk em P1).
    /// </summary>
    Task<int> ContarFalhasPermanentesAsync(
        Guid empresaId, Guid faturaPagamentoId, CancellationToken ct = default);

    /// <summary>
    /// Registra audit event (transicao de status). Usado pelo orchestrator e
    /// pelo reconciliador (P1).
    /// </summary>
    Task AdicionarEventoAsync(PaymentAttemptEvent evento, CancellationToken ct = default);

    /// <summary>
    /// Atualiza attempt (status + LatencyMs + GatewayTransactionId + erro).
    /// O caller modifica a entity e chama esse metodo para persistir + emitir audit.
    /// </summary>
    Task AtualizarAsync(PaymentAttempt attempt, string motivoEvento, CancellationToken ct = default);
}
