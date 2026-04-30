using EasyStock.Api.Http;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Reflection;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/status")]
[Authorize(Policy = "SuperAdmin")]
[ResponseCache(Duration = 30)]
public class AdminStatusController(EasyStockDbContext db) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var agora = DateTime.UtcNow;

        // Database health
        string dbStatus;
        long dbLatencyMs = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            sw.Stop();
            dbLatencyMs = sw.ElapsedMilliseconds;
            dbStatus = dbLatencyMs < 200 ? "ok" : dbLatencyMs < 1000 ? "degraded" : "degraded";
        }
        catch
        {
            dbStatus = "down";
        }

        // API uptime
        var startTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeSeconds = (long)(agora - startTime).TotalSeconds;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        // Errors last 24h / last 1h
        var cutoff24h = agora.AddHours(-24);
        var cutoff1h = agora.AddHours(-1);
        var erros24h = await db.AuditLogs.CountAsync(x => !x.Sucesso && x.DataHora >= cutoff24h);
        var erros1h = await db.AuditLogs.CountAsync(x => !x.Sucesso && x.DataHora >= cutoff1h);

        // Active users last 24h
        var usuariosAtivos24h = await db.AuditLogs
            .Where(x => x.DataHora >= cutoff24h)
            .Select(x => x.UsuarioId)
            .Distinct()
            .CountAsync();

        // IA usage this month
        var iaGeracoesMes = await db.UsoIa
            .Where(x => x.Ano == agora.Year && x.Mes == agora.Month)
            .SumAsync(x => (int?)x.TotalGeracoes) ?? 0;

        // Open tickets
        var ticketsAbertos = await db.AdminTickets
            .CountAsync(x => x.Status == TicketStatus.Aberto);

        // Recent errors
        var errosRecentes = await db.AuditLogs
            .Where(x => !x.Sucesso)
            .OrderByDescending(x => x.DataHora)
            .Take(5)
            .Select(x => new { x.Acao, x.Detalhes, x.DataHora })
            .ToListAsync();

        return DataOk(new
        {
            database = new { status = dbStatus, latencyMs = dbLatencyMs },
            api = new { status = "ok", uptimeSeconds, version },
            erros24h = new { total = erros24h, ultimaHora = erros1h },
            uso = new { usuariosAtivos24h, iaGeracoesMes, ticketsAbertos },
            errosRecentes,
            ultimaVerificacao = agora
        });
    }
}
