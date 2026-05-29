using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — processa eventos pendentes via <see cref="INotificadorService"/> e
/// detecta rotinas Cron disparáveis. Sem loop, sem sleep — invocada por wrapper hosted ou trigger HTTP.
/// Emite métricas <c>notifications.avaliador.run.duration</c> e
/// <c>notifications.avaliador.events_processed</c>.
/// </summary>
public sealed class NotificacoesAvaliadorOrchestrator(
    INotificadorService notificadorService,
    IEventoNotificacaoRepository eventoRepo,
    IRotinaRepository rotinaRepo,
    RotinaScheduler rotinaScheduler,
    ILogger<NotificacoesAvaliadorOrchestrator> logger) : INotificacoesAvaliadorOrchestrator
{
    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "notifications.avaliador.run.duration", "ms",
        "Duração de 1 rodada do avaliador (eventos pendentes + cron rotinas)");
    private static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>(
        "notifications.avaliador.events_processed", "events",
        "Total de eventos avaliados pelo avaliador");

    public async Task ExecutarRodadaAsync(TimeSpan janelaAvaliacao, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Processa eventos pendentes (criados por coletores de estado / publicação direta)
            var pendentes = await eventoRepo.ListarPendentesAsync(limit: 200, ct);
            foreach (var evento in pendentes)
            {
                try
                {
                    await notificadorService.AvaliarEventoAsync(evento, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao avaliar evento {EventoId}", evento.Id);
                }
            }

            if (pendentes.Count > 0)
            {
                logger.LogInformation("AvaliadorOrchestrator: processados {Count} eventos pendentes.", pendentes.Count);
                EventsProcessed.Add(pendentes.Count);
            }

            // 2. Detecta rotinas Cron disparáveis (eventos serão criados pelos coletores de estado)
            var agora = DateTime.UtcNow;
            var ultimaExecucao = agora - (janelaAvaliacao > TimeSpan.Zero ? janelaAvaliacao : TimeSpan.FromMinutes(2));
            var rotinasAtivas = await rotinaRepo.ListarAtivasAsync(ct: ct);

            foreach (var rotina in rotinasAtivas.Where(r => r.TriggerTipo == TriggerTipoRotina.Cron))
            {
                if (!rotinaScheduler.DeveriasExecutar(rotina, ultimaExecucao, agora))
                    continue;

                logger.LogInformation(
                    "Rotina cron {Codigo} matched (eventos serão criados pelos coletores).",
                    rotina.Codigo);
            }
        }
        finally
        {
            sw.Stop();
            RunDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }
}
