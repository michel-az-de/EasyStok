using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IClienteSessionRepository
{
    /// <summary>Lookup por sid (UUID embutido no JWT). Retorna null se não existe.</summary>
    Task<ClienteSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ClienteSession session, CancellationToken ct = default);
    Task UpdateAsync(ClienteSession session, CancellationToken ct = default);
}
