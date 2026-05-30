using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do read model de status Admin (F7). Inclui o
/// health-check "SELECT 1" (com latência) + métricas agregadas cross-tenant.
/// </summary>
public sealed class AdminStatusQueries(
    EasyStockDbContext db,
    ILogger<AdminStatusQueries> logger) : IAdminStatusQueries
{
    public async Task<AdminStatusData> GetStatusAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        string dbStatus;
        long dbLatencyMs = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            sw.Stop();
            dbLatencyMs = sw.ElapsedMilliseconds;
            dbStatus = dbLatencyMs < 200 ? "ok" : "degraded";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health-check do banco falhou no AdminStatus");
            dbStatus = "down";
        }

        var cutoff24h = nowUtc.AddHours(-24);
        var cutoff1h = nowUtc.AddHours(-1);

        var erros24h = await db.AuditLogs.CountAsync(x => !x.Sucesso && x.DataHora >= cutoff24h, ct);
        var erros1h = await db.AuditLogs.CountAsync(x => !x.Sucesso && x.DataHora >= cutoff1h, ct);

        var usuariosAtivos24h = await db.AuditLogs
            .Where(x => x.DataHora >= cutoff24h)
            .Select(x => x.UsuarioId)
            .Distinct()
            .CountAsync(ct);

        var iaGeracoesMes = await db.UsoIa
            .Where(x => x.Ano == nowUtc.Year && x.Mes == nowUtc.Month)
            .SumAsync(x => (int?)x.TotalGeracoes, ct) ?? 0;

        var ticketsAbertos = await db.AdminTickets
            .CountAsync(x => x.Status == TicketStatus.Aberto, ct);

        var errosRecentes = await db.AuditLogs
            .Where(x => !x.Sucesso)
            .OrderByDescending(x => x.DataHora)
            .Take(5)
            .Select(x => new AdminStatusErroRecente(x.Acao, x.Detalhes, x.DataHora))
            .ToListAsync(ct);

        return new AdminStatusData(
            dbStatus, dbLatencyMs, erros24h, erros1h,
            usuariosAtivos24h, iaGeracoesMes, ticketsAbertos, errosRecentes);
    }
}
