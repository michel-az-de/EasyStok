using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class ClienteSessionRepository(EasyStockDbContext db) : IClienteSessionRepository
{
    public Task<ClienteSession?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ClienteSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task AddAsync(ClienteSession session, CancellationToken ct = default)
    {
        db.ClienteSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ClienteSession session, CancellationToken ct = default)
    {
        db.ClienteSessions.Update(session);
        return Task.CompletedTask;
    }
}
