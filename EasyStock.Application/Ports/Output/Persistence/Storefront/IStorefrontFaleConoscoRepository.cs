using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IStorefrontFaleConoscoRepository
{
    Task<StorefrontFaleConosco?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<StorefrontFaleConosco>> GetByStorefrontAsync(Guid storefrontId, int max = 100, CancellationToken ct = default);
    Task AddAsync(StorefrontFaleConosco mensagem, CancellationToken ct = default);
    Task UpdateAsync(StorefrontFaleConosco mensagem, CancellationToken ct = default);
}
