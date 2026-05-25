using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Onda 2.2 — repositorio de subscriptions de Web Push.
/// </summary>
public interface IWebPushSubscriptionRepository
{
    Task<WebPushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<IReadOnlyList<WebPushSubscription>> GetByUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<WebPushSubscription>> GetByEmpresaAsync(Guid empresaId, CancellationToken ct = default);
    Task AddAsync(WebPushSubscription sub, CancellationToken ct = default);
    Task UpdateAsync(WebPushSubscription sub, CancellationToken ct = default);
    Task DesativarAsync(string endpoint, CancellationToken ct = default);
}
