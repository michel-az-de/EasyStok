using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IFreteZonaRepository
{
    Task<FreteZona?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FreteZona>> GetAtivasDoStorefrontOrdenadasAsync(Guid storefrontId, CancellationToken ct = default);
    Task<IReadOnlyList<FreteZona>> GetTodasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(FreteZona zona, CancellationToken ct = default);
    Task UpdateAsync(FreteZona zona, CancellationToken ct = default);
}
