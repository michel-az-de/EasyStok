using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

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
        var entry = db.Entry(session);
        if (entry.State == EntityState.Detached) db.ClienteSessions.Attach(session);
        if (entry.State == EntityState.Added) return Task.CompletedTask;
        // Uma renovacao iniciada antes do logout nunca pode gravar Revogada=false.
        entry.Property(s => s.UltimoUsoEm).IsModified = true;
        entry.Property(s => s.Revogada).IsModified = session.Revogada;
        entry.Property(s => s.MotivoRevogacao).IsModified = session.Revogada;
        return Task.CompletedTask;
    }
}
