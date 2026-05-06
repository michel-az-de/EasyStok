using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface INotificadorService
{
    /// <summary>
    /// Publica evento + cria entradas no outbox na mesma transação.
    /// Chamado por use cases da API para notificações event-driven imediatas.
    /// </summary>
    Task PublicarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        Guid? usuarioDestinoId,
        string payloadJson,
        IDictionary<string, object?>? varsAdicionais = null,
        CancellationToken ct = default);

    /// <summary>
    /// Processa um EventoNotificacao já persistido, criando entradas no outbox.
    /// Chamado pelo Worker para eventos gerados por coletores/cron.
    /// </summary>
    Task AvaliarEventoAsync(
        EventoNotificacao evento,
        CancellationToken ct = default);
}
