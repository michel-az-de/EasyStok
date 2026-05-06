using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IConsentimentoRepository
{
    Task<ConsentimentoNotificacao?> GetAsync(
        Guid usuarioId,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConsentimentoNotificacao>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken ct = default);

    Task AddAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default);
    Task UpdateAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default);
}
