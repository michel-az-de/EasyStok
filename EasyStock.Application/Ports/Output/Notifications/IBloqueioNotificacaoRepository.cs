using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IBloqueioNotificacaoRepository
{
    Task<BloqueioNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BloqueioNotificacao>> ListarAtivosAsync(
        Guid? empresaId,
        CanalNotificacao? canal = null,
        CancellationToken ct = default);

    Task AddAsync(BloqueioNotificacao bloqueio, CancellationToken ct = default);
    Task UpdateAsync(BloqueioNotificacao bloqueio, CancellationToken ct = default);
}
