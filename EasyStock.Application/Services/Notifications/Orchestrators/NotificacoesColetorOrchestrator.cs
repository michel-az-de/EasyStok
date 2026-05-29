using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — agrega todos os <see cref="IColetorEventoNotificacao"/> registrados
/// no DI e os executa sequencialmente. Falha de um coletor não interrompe os demais.
/// Emite métrica <c>notifications.collector.run.duration</c> (Histogram, ms) por coletor.
/// </summary>
public sealed class NotificacoesColetorOrchestrator(
    IEnumerable<IColetorEventoNotificacao> coletores,
    ILogger<NotificacoesColetorOrchestrator> logger) : INotificacoesColetorOrchestrator
{
    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Histogram<double> CollectorDuration = Meter.CreateHistogram<double>(
        "notifications.collector.run.duration", "ms",
        "Duração de 1 rodada de cada coletor de eventos de estado");

    public async Task ExecutarRodadaAsync(CancellationToken ct = default)
    {
        var lista = coletores.ToList();
        if (lista.Count == 0)
        {
            logger.LogDebug("ColetorOrchestrator: nenhum coletor registrado.");
            return;
        }

        foreach (var coletor in lista)
        {
            var nome = coletor.GetType().Name;
            var sw = Stopwatch.StartNew();
            try
            {
                await coletor.ColetarAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no coletor {Coletor}.", nome);
            }
            finally
            {
                sw.Stop();
                CollectorDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new TagList { { "collector", nome } });
            }
        }
    }
}
