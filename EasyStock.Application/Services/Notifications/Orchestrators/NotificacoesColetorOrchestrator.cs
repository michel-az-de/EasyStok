using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — agrega todos os <see cref="IColetorEventoNotificacao"/> registrados
/// no DI e os executa sequencialmente. Falha de um coletor não interrompe os demais.
/// </summary>
public sealed class NotificacoesColetorOrchestrator(
    IEnumerable<IColetorEventoNotificacao> coletores,
    ILogger<NotificacoesColetorOrchestrator> logger) : INotificacoesColetorOrchestrator
{
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
            try
            {
                await coletor.ColetarAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no coletor {Coletor}.", coletor.GetType().Name);
            }
        }
    }
}
