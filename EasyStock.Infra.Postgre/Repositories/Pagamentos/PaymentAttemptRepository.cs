using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Pagamentos;

public sealed class PaymentAttemptRepository(EasyStockDbContext db) : IPaymentAttemptRepository
{
    public async Task AdicionarAsync(PaymentAttempt attempt, string motivoEvento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await db.PaymentAttempts.AddAsync(attempt, ct);

        var evento = PaymentAttemptEvent.Registrar(
            paymentAttemptId: attempt.Id,
            empresaId: attempt.EmpresaId,
            fromStatus: null,
            toStatus: attempt.Status,
            motivo: motivoEvento);
        await db.PaymentAttemptEvents.AddAsync(evento, ct);
    }

    public Task<PaymentAttempt?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.PaymentAttempts
            .FirstOrDefaultAsync(a => a.EmpresaId == empresaId && a.Id == id, ct);

    public async Task<IReadOnlyList<PaymentAttempt>> ListarPorFaturaPagamentoAsync(
        Guid empresaId, Guid faturaPagamentoId, CancellationToken ct = default)
    {
        return await db.PaymentAttempts
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.FaturaPagamentoId == faturaPagamentoId)
            .OrderBy(a => a.Tentativa)
            .ToListAsync(ct);
    }

    public Task<int> ContarFalhasPermanentesAsync(
        Guid empresaId, Guid faturaPagamentoId, CancellationToken ct = default) =>
        db.PaymentAttempts
            .CountAsync(a =>
                a.EmpresaId == empresaId &&
                a.FaturaPagamentoId == faturaPagamentoId &&
                a.Status == StatusPaymentAttempt.FalhaPermanente, ct);

    public async Task AdicionarEventoAsync(PaymentAttemptEvent evento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        await db.PaymentAttemptEvents.AddAsync(evento, ct);
    }

    public async Task AtualizarAsync(PaymentAttempt attempt, string motivoEvento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var entry = db.Entry(attempt);
        var fromStatus = entry.Property(a => a.Status).OriginalValue;
        // Quando state e Detached/Added, OriginalValue == CurrentValue. Aceitavel —
        // audit grava transicao do que o caller acabou de mudar.
        db.PaymentAttempts.Update(attempt);

        var evento = PaymentAttemptEvent.Registrar(
            paymentAttemptId: attempt.Id,
            empresaId: attempt.EmpresaId,
            fromStatus: fromStatus,
            toStatus: attempt.Status,
            motivo: motivoEvento);
        await db.PaymentAttemptEvents.AddAsync(evento, ct);
    }
}
