using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Enums.Notifications;
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
        var notificador = scope.ServiceProvider.GetService<INotificadorService>();
        var hoje = DateTime.UtcNow.Date;

        // Cohorts D-3 e D-1 (parcelas vencendo)
        var d3Inicio = hoje.AddDays(2);
        var d3Fim = hoje.AddDays(3);
        var d1Inicio = hoje;
        var d1Fim = hoje.AddDays(1);

        var processadasD3 = 0;
        var processadasD1 = 0;
        var processadasVencidas = 0;
        var contasAtualizadas = new HashSet<(Guid contaId, TipoLadoFinanceiro lado)>();

        // ── ContaPagar — D-3 ─────────────────────────────────────────────
        var parcelasD3Pagar = await db.ParcelasPagar
            .IgnoreQueryFilters()
            .Include(p => p.ContaPagar)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date >= d3Inicio && p.DataVencimento.Date < d3Fim &&
                p.NotificadaD3Em == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var p in parcelasD3Pagar)
        {
            await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaPagarVencendo, p, "d3", ct);
            p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaD3, DateTime.UtcNow);
            processadasD3++;
        }

        // ── ContaReceber — D-3 ───────────────────────────────────────────
        var parcelasD3Receber = await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Include(p => p.ContaReceber)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date >= d3Inicio && p.DataVencimento.Date < d3Fim &&
                p.NotificadaD3Em == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var p in parcelasD3Receber)
        {
            await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaReceberVencendo, p, "d3", ct);
            p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaD3, DateTime.UtcNow);
            processadasD3++;
        }

        // ── D-1 ─────────────────────────────────────────────────────────
        var parcelasD1Pagar = await db.ParcelasPagar
            .IgnoreQueryFilters()
            .Include(p => p.ContaPagar)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date >= d1Inicio && p.DataVencimento.Date < d1Fim &&
                p.NotificadaD1Em == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var p in parcelasD1Pagar)
        {
            await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaPagarVencendo, p, "d1", ct);
            p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaD1, DateTime.UtcNow);
            processadasD1++;
        }
        var parcelasD1Receber = await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Include(p => p.ContaReceber)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date >= d1Inicio && p.DataVencimento.Date < d1Fim &&
                p.NotificadaD1Em == null)
            .Take(500)
            .ToListAsync(ct);
        foreach (var p in parcelasD1Receber)
        {
            await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaReceberVencendo, p, "d1", ct);
            p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaD1, DateTime.UtcNow);
            processadasD1++;
        }

        // ── D+0 (vencidas) — marcar status e notificar ─────────────────
        var parcelasPagar = await db.ParcelasPagar
            .IgnoreQueryFilters()
            .Include(p => p.ContaPagar)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date < hoje)
            .Take(2000)
            .ToListAsync(ct);
        foreach (var p in parcelasPagar)
        {
            p.MarcarVencidaSeAplicavel(hoje);
            if (p.NotificadaVencidaEm is null)
            {
                await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaPagarVencida, p, "vencida", ct);
                p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaVencida, DateTime.UtcNow);
                processadasVencidas++;
            }
            contasAtualizadas.Add((p.ContaPagarId, TipoLadoFinanceiro.Pagar));
        }
        var parcelasReceber = await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Include(p => p.ContaReceber)
            .Where(p =>
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                p.DataVencimento.Date < hoje)
            .Take(2000)
            .ToListAsync(ct);
        foreach (var p in parcelasReceber)
        {
            p.MarcarVencidaSeAplicavel(hoje);
            if (p.NotificadaVencidaEm is null)
            {
                await PublicarNotificacaoAsync(notificador, TipoEventoNotificacao.ContaReceberVencida, p, "vencida", ct);
                p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaVencida, DateTime.UtcNow);
                processadasVencidas++;
            }
            contasAtualizadas.Add((p.ContaReceberId, TipoLadoFinanceiro.Receber));
        }

        // Atualiza status agregado das contas afetadas
        foreach (var (contaId, lado) in contasAtualizadas)
        {
            if (lado == TipoLadoFinanceiro.Pagar)
            {
                var conta = await db.ContasPagar.IgnoreQueryFilters()
                    .Include(c => c.Parcelas).FirstOrDefaultAsync(c => c.Id == contaId, ct);
                conta?.AtualizarStatusPorParcelas();
            }
            else
            {
                var conta = await db.ContasReceber.IgnoreQueryFilters()
                    .Include(c => c.Parcelas).FirstOrDefaultAsync(c => c.Id == contaId, ct);
                conta?.AtualizarStatusPorParcelas();
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ContaFinanceiraVencimentoJob: D-3={D3} D-1={D1} Vencidas={Vencidas}. Contas afetadas={Contas}",
            processadasD3, processadasD1, processadasVencidas, contasAtualizadas.Count);
    }

    private async Task PublicarNotificacaoAsync(
        INotificadorService? notificador,
        TipoEventoNotificacao tipo,
        ParcelaPagar parcela,
        string variante,
        CancellationToken ct)
    {
        if (notificador is null) return;
        try
        {
            await notificador.PublicarEventoAsync(
                tipo, parcela.EmpresaId, usuarioDestinoId: null,
                payloadJson: JsonSerializer.Serialize(new
                {
                    parcelaId = parcela.Id,
                    contaId = parcela.ContaPagarId,
                    numero = parcela.Numero,
                    valor = parcela.Saldo,
                    vencimento = parcela.DataVencimento,
                    descricao = parcela.ContaPagar?.Descricao,
                    variante
                }), ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao publicar evento {Tipo} pra parcela {ParcelaId}", tipo, parcela.Id);
        }
    }

    private async Task PublicarNotificacaoAsync(
        INotificadorService? notificador,
        TipoEventoNotificacao tipo,
        ParcelaReceber parcela,
        string variante,
        CancellationToken ct)
    {
        if (notificador is null) return;
        try
        {
            await notificador.PublicarEventoAsync(
                tipo, parcela.EmpresaId, usuarioDestinoId: null,
                payloadJson: JsonSerializer.Serialize(new
                {
                    parcelaId = parcela.Id,
                    contaId = parcela.ContaReceberId,
                    numero = parcela.Numero,
                    valor = parcela.Saldo,
                    vencimento = parcela.DataVencimento,
                    descricao = parcela.ContaReceber?.Descricao,
                    variante
                }), ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao publicar evento {Tipo} pra parcela {ParcelaId}", tipo, parcela.Id);
        }
    }
}
