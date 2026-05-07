using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Anonimiza dados pessoais (Destinatario e CorpoRenderizado) em mensagens do outbox
/// com mais de <see cref="WorkerOptions.RetencaoLogsDias"/> dias, preservando metadados
/// estatísticos. Roda uma vez por dia (hora configurável UTC).
/// </summary>
public sealed class AnonimizarLogsAntigosService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<AnonimizarLogsAntigosService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnonimizarLogsAntigosService iniciado");

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

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            await ExecutarAnonimizacaoAsync(opts.RetencaoLogsDias, stoppingToken);
        }
    }

    private async Task ExecutarAnonimizacaoAsync(int retencaoDias, CancellationToken ct)
    {
        var limiteAnonimizacao = DateTime.UtcNow.AddDays(-retencaoDias);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

            // Anonimiza OutboxMensagemNotificacao — apaga destinatário e corpo renderizado
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

            logger.LogInformation(
                "Anonimização: {TotalOutbox} mensagens outbox e {TotalLogs} logs de envio anonimizados (>{Dias}d)",
                totalOutbox, totalLogs, retencaoDias);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro durante anonimização de logs antigos");
        }
    }
}
