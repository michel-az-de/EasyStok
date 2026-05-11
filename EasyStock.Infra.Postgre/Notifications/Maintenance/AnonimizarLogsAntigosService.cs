using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Postgre.Notifications.Maintenance;

/// <summary>
/// Anonimiza dados pessoais (Destinatario, CorpoRenderizado, RespostaProviderJson, ErroDetalhado)
/// em mensagens do outbox e logs de envio com mais de
/// <see cref="NotificationsHostingOptions.RetencaoLogsDias"/> dias, preservando metadados
/// estatísticos. Roda 1x ao iniciar (catch-up) e diariamente na hora configurada UTC.
/// <para>
/// Movido de EasyStock.Worker/BackgroundServices para uso unificado por Worker e API
/// (ambos resolvem via Mode=Hosted no AddNotificationsHosting). Idempotente — re-rodar
/// não muda registros já anonimizados (filtro != "[anonimizado]").
/// </para>
/// </summary>
public sealed class AnonimizarLogsAntigosService(
    IServiceProvider serviceProvider,
    IOptions<NotificationsHostingOptions> options,
    ILogger<AnonimizarLogsAntigosService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnonimizarLogsAntigosService iniciado");

        // Catch-up no startup: se o processo passou da hora alvo enquanto estava down
        // (restarts frequentes, deploy), roda imediatamente uma vez.
        try { await ExecutarAnonimizacaoAsync(options.Value.RetencaoLogsDias, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.Value;
            var now = DateTime.UtcNow;
            var proximaExecucao = now.Date.AddHours(opts.AnonimizarHoraUtc);
            if (proximaExecucao <= now)
                proximaExecucao = proximaExecucao.AddDays(1);

            var delay = proximaExecucao - now;
            logger.LogDebug(
                "AnonimizarLogsAntigosService: próxima execução em {ProximaExecucao} (daqui {Delay})",
                proximaExecucao, delay);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            await ExecutarAnonimizacaoAsync(opts.RetencaoLogsDias, stoppingToken);
        }
    }

    private async Task ExecutarAnonimizacaoAsync(int retencaoDias, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int totalAnonimizado = 0;
        string status = "OK";
        string? detalhe = null;
        var limiteAnonimizacao = DateTime.UtcNow.AddDays(-retencaoDias);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

            var totalOutbox = await db.NotifOutboxMensagens
                .Where(m => m.CriadoEm < limiteAnonimizacao
                         && m.Status != StatusOutbox.Pendente
                         && m.Destinatario != "[anonimizado]")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Destinatario, "[anonimizado]")
                    .SetProperty(m => m.CorpoRenderizado, "[anonimizado]"),
                    ct);

            var totalLogs = await db.NotifLogsEnvio
                .Where(l => l.OcorridoEm < limiteAnonimizacao
                         && (l.RespostaProviderJson != null || l.ErroDetalhado != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.RespostaProviderJson, (string?)null)
                    .SetProperty(l => l.ErroDetalhado, (string?)null),
                    ct);

            totalAnonimizado = totalOutbox + totalLogs;
            detalhe = $"outbox={totalOutbox} logs={totalLogs}";
            logger.LogInformation(
                "Anonimização: {TotalOutbox} mensagens outbox e {TotalLogs} logs de envio anonimizados (>{Dias}d)",
                totalOutbox, totalLogs, retencaoDias);
        }
        catch (Exception ex)
        {
            status = "Erro";
            detalhe = ex.GetType().Name + ": " + ex.Message;
            logger.LogError(ex, "Erro durante anonimização de logs antigos");
        }
        finally
        {
            sw.Stop();
            await GravarHeartbeatAsync("AnonimizarLogs", status, detalhe,
                totalAnonimizado, (int)sw.ElapsedMilliseconds, ct);
        }
    }

    private async Task GravarHeartbeatAsync(
        string servico, string status, string? detalhe,
        int? itensProcessados, int? duracaoMs, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IHeartbeatRecorder>();
            await recorder.RecordAsync(servico, status, detalhe, itensProcessados, duracaoMs, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao gravar heartbeat de AnonimizarLogs");
        }
    }
}
