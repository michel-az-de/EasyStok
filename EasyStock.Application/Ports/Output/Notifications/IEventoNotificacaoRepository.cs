using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IEventoNotificacaoRepository
{
    Task<EventoNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>O evento da empresa pelo id, rastreado. A empresa vai no <c>WHERE</c> porque no Worker o filtro do EF está desligado.</summary>
    Task<EventoNotificacao?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EventoNotificacao>> ListarPendentesAsync(
        int limit = 100,
        CancellationToken ct = default);

    Task AddAsync(EventoNotificacao evento, CancellationToken ct = default);
    Task UpdateAsync(EventoNotificacao evento, CancellationToken ct = default);
}
