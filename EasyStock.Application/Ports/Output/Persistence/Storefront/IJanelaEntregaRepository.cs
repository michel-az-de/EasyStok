using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IJanelaEntregaRepository
{
    Task<JanelaEntrega?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<JanelaEntrega>> GetAtivasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(JanelaEntrega janela, CancellationToken ct = default);
    Task UpdateAsync(JanelaEntrega janela, CancellationToken ct = default);
}
