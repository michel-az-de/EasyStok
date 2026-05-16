using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

/// <summary>Onda 2.2 — repo de subscriptions de Web Push.</summary>
public sealed class WebPushSubscriptionRepository(EasyStockDbContext db) : IWebPushSubscriptionRepository
{
    public Task<WebPushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken ct = default) =>
        db.Set<WebPushSubscription>().FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

    public async Task<IReadOnlyList<WebPushSubscription>> GetByUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        await db.Set<WebPushSubscription>()
            .AsNoTracking()
            .Where(s => s.UsuarioId == usuarioId && s.Ativo)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WebPushSubscription>> GetByEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await db.Set<WebPushSubscription>()
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .ToListAsync(ct);

    public Task AddAsync(WebPushSubscription sub, CancellationToken ct = default)
    {
        db.Set<WebPushSubscription>().Add(sub);
        return db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(WebPushSubscription sub, CancellationToken ct = default)
    {
        db.Set<WebPushSubscription>().Update(sub);
        return db.SaveChangesAsync(ct);
    }

    public async Task DesativarAsync(string endpoint, CancellationToken ct = default)
    {
        await db.Set<WebPushSubscription>()
            .Where(s => s.Endpoint == endpoint)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Ativo, false), ct);
    }
}
