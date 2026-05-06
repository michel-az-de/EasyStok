using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IEventoNotificacaoRepository
{
    Task<EventoNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EventoNotificacao>> ListarPendentesAsync(
        int limit = 100,
        CancellationToken ct = default);

    Task AddAsync(EventoNotificacao evento, CancellationToken ct = default);
    Task UpdateAsync(EventoNotificacao evento, CancellationToken ct = default);
}
