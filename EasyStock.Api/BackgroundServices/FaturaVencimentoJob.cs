using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job diario de notificacoes de vencimento de fatura (F6).
///
/// <para>
/// Roda 1x/dia (proxima execucao 09:00 UTC apos boot) e processa 3 cohorts:
/// </para>
/// <list type="bullet">
///   <item><b>D-3</b>: faturas Emitida/ParcialmentePaga vencendo entre +2 e +3
///   dias → publica <see cref="TipoEventoNotificacao.FaturaVencendo"/> e
///   carimba <see cref="TipoEventoFatura.NotificadaVencendoD3"/> em
///   <see cref="FaturaEvento"/> (idempotencia anti-duplicacao).</item>
///   <item><b>D-1</b>: vencendo entre 0 e +1 dia → mesmo evento mas com
///   <see cref="TipoEventoFatura.NotificadaVencendoD1"/>.</item>
///   <item><b>D+0+</b>: ja vencidas → marca <see cref="StatusFatura.Vencida"/>
///   (via <see cref="Fatura.MarcarVencidaSeAplicavel"/>) e publica
///   <see cref="TipoEventoNotificacao.FaturaVencida"/>.</item>
/// </list>
///
/// <para>
/// Idempotencia: antes de notificar, checa se ja existe FaturaEvento com o
/// tipo correspondente — evita spam se o job rodar 2x no mesmo dia ou apos
/// restart.
/// </para>
/// </summary>
public sealed class FaturaVencimentoJob(
    IServiceProvider serviceProvider,
    ILogger<FaturaVencimentoJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x4661_7475_5665_6E63L; // "FaturVenc"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FaturaVencimentoJob iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddDays(1).AddHours(9); // 09:00 UTC proximo dia
                if (now.Hour < 9) nextRun = now.Date.AddHours(9);

                var delay = nextRun - now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                await RunWithAdvisoryLockAsync(LockKeyJob, ProcessarAsync, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "FaturaVencimentoJob: erro fatal — aguardando 1h.");
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
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var p = cmd.CreateParameter();
            p.ParameterName = "key"; p.Value = key;
            cmd.Parameters.Add(p);
            var got = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!got)
            {
                logger.LogInformation("FaturaVencimentoJob: outra replica processando. Pulando.");
                return;
            }
            try { await action(ct); }
            finally
            {
                using var unlock = db.Database.GetDbConnection().CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                var pu = unlock.CreateParameter();
                pu.ParameterName = "key"; pu.Value = key;
                unlock.Parameters.Add(pu);
                await unlock.ExecuteScalarAsync(ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task ProcessarAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var notificador = scope.ServiceProvider.GetRequiredService<INotificadorService>();

        var hoje = DateTime.UtcNow.Date;
        var d3Inicio = hoje.AddDays(2);  // vence em 2-3 dias
        var d3Fim = hoje.AddDays(4);
        var d1Inicio = hoje;             // vence em 0-1 dias
        var d1Fim = hoje.AddDays(2);

        var totalD3 = await NotificarCohort(db, notificador, d3Inicio, d3Fim,
            TipoEventoFatura.NotificadaVencendoD3,
            TipoEventoNotificacao.FaturaVencendo,
            "d3", ct);

        var totalD1 = await NotificarCohort(db, notificador, d1Inicio, d1Fim,
            TipoEventoFatura.NotificadaVencendoD1,
            TipoEventoNotificacao.FaturaVencendo,
            "d1", ct);

        var totalVencidas = await ProcessarVencidasAsync(db, notificador, ct);

        logger.LogInformation(
            "FaturaVencimentoJob: rodada concluida. D-3 notificadas={D3} D-1 notificadas={D1} Vencidas={V}",
            totalD3, totalD1, totalVencidas);
    }

    private async Task<int> NotificarCohort(
        EasyStockDbContext db,
        INotificadorService notificador,
        DateTime vencimentoDe,
        DateTime vencimentoAte,
        TipoEventoFatura tipoEventoLocal,
        TipoEventoNotificacao tipoEventoNotif,
        string variante,
        CancellationToken ct)
    {
        // Faturas em estado "esperando pagamento" com vencimento na janela.
        var candidatas = await db.Faturas
            .IgnoreQueryFilters()
            .Where(f => (f.Status == StatusFatura.Emitida || f.Status == StatusFatura.ParcialmentePaga)
                && f.DataVencimento >= vencimentoDe
                && f.DataVencimento < vencimentoAte)
            .ToListAsync(ct);

        if (candidatas.Count == 0) return 0;

        // Anti-duplicacao: ja existe evento desse tipo para essas faturas?
        var ids = candidatas.Select(f => f.Id).ToList();
        var jaNotificadas = await db.FaturaEventos
            .IgnoreQueryFilters()
            .Where(e => ids.Contains(e.FaturaId) && e.Tipo == tipoEventoLocal)
            .Select(e => e.FaturaId)
            .ToHashSetAsync(ct);

        var notificadas = 0;
        foreach (var fatura in candidatas)
        {
            if (jaNotificadas.Contains(fatura.Id)) continue;

            try
            {
                await notificador.PublicarEventoAsync(
                    tipoEventoNotif,
                    fatura.EmpresaId,
                    usuarioDestinoId: null,
                    payloadJson: JsonSerializer.Serialize(new
                    {
                        faturaId = fatura.Id,
                        numero = fatura.Numero,
                        total = fatura.Total,
                        moeda = fatura.Moeda,
                        vencimento = fatura.DataVencimento,
                        variante
                    }),
                    ct: ct);

                db.FaturaEventos.Add(FaturaEvento.Criar(
                    fatura.Id,
                    tipoEventoLocal,
                    origem: "job-vencimento",
                    metadadosJson: JsonSerializer.Serialize(new { variante, vencimento = fatura.DataVencimento })));

                notificadas++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "FaturaVencimentoJob: falha ao notificar {Variante} fatura {FaturaId}",
                    variante, fatura.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        return notificadas;
    }

    private async Task<int> ProcessarVencidasAsync(
        EasyStockDbContext db,
        INotificadorService notificador,
        CancellationToken ct)
    {
        var hoje = DateTime.UtcNow.Date;

        var candidatas = await db.Faturas
            .IgnoreQueryFilters()
            .Where(f => (f.Status == StatusFatura.Emitida || f.Status == StatusFatura.ParcialmentePaga)
                && f.DataVencimento < hoje)
            .Take(500)
            .ToListAsync(ct);

        if (candidatas.Count == 0) return 0;

        var marcadas = 0;
        foreach (var fatura in candidatas)
        {
            try
            {
                fatura.MarcarVencidaSeAplicavel();
                if (fatura.Status != StatusFatura.Vencida) continue;

                db.FaturaEventos.Add(FaturaEvento.Criar(
                    fatura.Id, TipoEventoFatura.Vencida, origem: "job-vencimento"));

                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.FaturaVencida,
                    fatura.EmpresaId,
                    usuarioDestinoId: null,
                    payloadJson: JsonSerializer.Serialize(new
                    {
                        faturaId = fatura.Id,
                        numero = fatura.Numero,
                        total = fatura.Total,
                        moeda = fatura.Moeda,
                        vencimento = fatura.DataVencimento,
                        diasAtraso = (hoje - fatura.DataVencimento.Date).Days
                    }),
                    ct: ct);

                marcadas++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "FaturaVencimentoJob: falha ao marcar vencida {FaturaId}", fatura.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        return marcadas;
    }
}
