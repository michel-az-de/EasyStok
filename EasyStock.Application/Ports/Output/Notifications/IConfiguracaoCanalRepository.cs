using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IConfiguracaoCanalRepository
{
    /// <summary>
    /// Retorna configuração empresa-specific ou global (null empresaId) se não houver override.
    /// </summary>
    Task<ConfiguracaoCanal?> GetAsync(
        CanalNotificacao canal,
        Guid? empresaId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConfiguracaoCanal>> ListarAsync(
        Guid? empresaId,
        CancellationToken ct = default);

    Task AddAsync(ConfiguracaoCanal config, CancellationToken ct = default);
    Task UpdateAsync(ConfiguracaoCanal config, CancellationToken ct = default);
}
