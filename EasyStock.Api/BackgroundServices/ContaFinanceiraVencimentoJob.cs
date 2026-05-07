using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job diario de vencimento de parcelas de Contas a Pagar/Receber (CAP/CAR).
///
/// <para>
/// Roda 1x/dia (proxima execucao 09:30 UTC apos boot — 30min depois do
/// <see cref="FaturaVencimentoJob"/>) e processa parcelas atrasadas.
/// </para>
///
/// <para>Idempotente:</para>
/// - Marcar parcela vencida e idempotente (status muda apenas se Pendente/Parcial).
/// - Carimbamento de notificacao via <c>NotificadaD3Em/D1Em/VencidaEm</c>
///   evita reprocesso no mesmo dia.
/// - Outbox de notificacoes nao implementado nesta versao (P1) — apenas
///   marca status. Templates ficam em todo Outbox quando ativados.
/// </summary>
public sealed class ContaFinanceiraVencimentoJob(
    IServiceProvider serviceProvider,
    ILogger<ContaFinanceiraVencimentoJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x4361704361725665L; // "CapCarVe"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ContaFinanceiraVencimentoJob iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var alvoHoraUtc = 9.5; // 09:30 UTC
                var nextRun = now.Date.AddHours(alvoHoraUtc);
                if (now.TimeOfDay.TotalHours >= alvoHoraUtc)
                    nextRun = now.Date.AddDays(1).AddHours(alvoHoraUtc);

                var delay = nextRun - now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                await RunWithAdvisoryLockAsync(LockKeyJob, ProcessarAsync, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ContaFinanceiraVencimentoJob: erro — aguardando 1h.");
                try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
                catch { break; }
            }
        }
    }

    private async Task RunWithAdvisoryLockAsync(long key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null || !db.Database.IsNpgsql())
        {
            await action(ct);
            return;
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = key; cmd.Parameters.Add(p);

            var got = (bool?)await cmd.ExecuteScalarAsync(ct);
            if (got != true)
            {
                logger.LogInformation("ContaFinanceiraVencimentoJob: outra instancia ja rodando — pulando.");
                return;
            }
            try { await action(ct); }
            finally
            {
                await using var rel = conn.CreateCommand();
                rel.CommandText = "SELECT pg_advisory_unlock(@k)";
                var rp = rel.CreateParameter(); rp.ParameterName = "k"; rp.Value = key; rel.Parameters.Add(rp);
                await rel.ExecuteScalarAsync(ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task ProcessarAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var hoje = DateTime.UtcNow.Date;

        // ── ContaPagar parcelas ───────────────────────────────────────────
        var parcelasPagar = await db.ParcelasPagar
            .IgnoreQueryFilters()
            .Where(p =>
                (p.Status == StatusParcela.Pendente ||
                 p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date < hoje)
            .Take(2000)
            .ToListAsync(ct);

        var contasPagarAtualizadas = new HashSet<Guid>();
        foreach (var p in parcelasPagar)
        {
            p.MarcarVencidaSeAplicavel(hoje);
            if (p.NotificadaVencidaEm is null)
                p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaVencida, DateTime.UtcNow);
            contasPagarAtualizadas.Add(p.ContaPagarId);
        }

        // Atualiza status agregado das contas afetadas
        foreach (var contaId in contasPagarAtualizadas)
        {
            var conta = await db.ContasPagar
                .IgnoreQueryFilters()
                .Include(c => c.Parcelas)
                .FirstOrDefaultAsync(c => c.Id == contaId, ct);
            conta?.AtualizarStatusPorParcelas();
        }

        // ── ContaReceber parcelas ─────────────────────────────────────────
        var parcelasReceber = await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Where(p =>
                (p.Status == StatusParcela.Pendente ||
                 p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date < hoje)
            .Take(2000)
            .ToListAsync(ct);

        var contasReceberAtualizadas = new HashSet<Guid>();
        foreach (var p in parcelasReceber)
        {
            p.MarcarVencidaSeAplicavel(hoje);
            if (p.NotificadaVencidaEm is null)
                p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaVencida, DateTime.UtcNow);
            contasReceberAtualizadas.Add(p.ContaReceberId);
        }

        foreach (var contaId in contasReceberAtualizadas)
        {
            var conta = await db.ContasReceber
                .IgnoreQueryFilters()
                .Include(c => c.Parcelas)
                .FirstOrDefaultAsync(c => c.Id == contaId, ct);
            conta?.AtualizarStatusPorParcelas();
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ContaFinanceiraVencimentoJob: {QtdPagar} parcelas a pagar e {QtdReceber} parcelas a receber marcadas vencidas. Contas afetadas: CP={ContasPagar} CR={ContasReceber}",
            parcelasPagar.Count, parcelasReceber.Count,
            contasPagarAtualizadas.Count, contasReceberAtualizadas.Count);
    }
}
