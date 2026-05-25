using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// F10-D — Background service que verifica dispositivos mobile com problemas
/// e gera alertas via log estruturado.
///
/// Critérios de alerta:
/// - Device offline há mais de 24h (LastSeenAt)
/// - Muitos devices de mesma empresa offline
///
/// Executa a cada 30 minutos. Alertas vão pro Serilog (dashboard existente).
/// Follow-up: integrar com NotificacaoGlobal para push no painel admin.
/// </summary>
public sealed class MobileAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MobileAlertService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

    public MobileAlertService(
        IServiceScopeFactory scopeFactory,
        ILogger<MobileAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDeviceHealthAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[MobileAlertService] Erro ao verificar saúde dos devices");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckDeviceHealthAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var now = DateTime.UtcNow;
        var staleAfter = now - StaleThreshold;

        // Devices ativos não-revogados que não dão sinal há >24h
        var staleDevices = await db.Set<MobileDevice>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => !d.Revoked && d.LastSeenAt < staleAfter)
            .Select(d => new { d.Id, d.DefaultOperatorName, d.EmpresaId, d.LastSeenAt })
            .ToListAsync(ct);

        if (staleDevices.Count == 0)
        {
            _logger.LogDebug("[MobileAlertService] Todos os devices ativos estão em dia.");
            return;
        }

        // Agrupa por empresa
        var byEmpresa = staleDevices.GroupBy(d => d.EmpresaId).ToList();

        foreach (var grp in byEmpresa)
        {
            var count = grp.Count();
            var oldest = grp.Min(d => d.LastSeenAt) ?? staleAfter;
            var hoursAgo = (now - oldest).TotalHours;

            if (count >= 3)
            {
                _logger.LogWarning(
                    "[MobileAlertService] ALERTA CRITICO: empresa {EmpresaId} tem {Count} devices offline ha mais de {Hours:F0}h. Dispositivos: {Devices}",
                    grp.Key, count, hoursAgo,
                    string.Join(", ", grp.Select(d => $"{d.DefaultOperatorName ?? d.Id} ({(now - (d.LastSeenAt ?? staleAfter)).TotalHours:F0}h)")));
            }
            else
            {
                _logger.LogWarning(
                    "[MobileAlertService] Device(s) offline: empresa {EmpresaId}, {Count} device(s) offline ha mais de {Hours:F0}h",
                    grp.Key, count, hoursAgo);
            }
        }

        _logger.LogInformation(
            "[MobileAlertService] Check concluido: {StaleCount} device(s) offline de {EmpresaCount} empresa(s)",
            staleDevices.Count, byEmpresa.Count);
    }
}
