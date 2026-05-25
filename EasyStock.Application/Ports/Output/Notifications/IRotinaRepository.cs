using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IRotinaRepository
{
    Task<RotinaNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RotinaNotificacao?> GetByCodigoAsync(string codigo, Guid? empresaId, CancellationToken ct = default);

    Task<IReadOnlyList<RotinaNotificacao>> ListarAtivasAsync(
        TipoEventoNotificacao? tipoEvento = null,
        CancellationToken ct = default);

    Task<(IReadOnlyList<RotinaNotificacao> Items, int Total)> ListarAsync(
        Guid? empresaId,
        bool? ativa = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task AddAsync(RotinaNotificacao rotina, CancellationToken ct = default);
    Task UpdateAsync(RotinaNotificacao rotina, CancellationToken ct = default);
}
