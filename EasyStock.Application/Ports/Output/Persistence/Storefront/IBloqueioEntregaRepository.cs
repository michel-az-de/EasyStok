using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IBloqueioEntregaRepository
{
    Task<BloqueioEntrega?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BloqueioEntrega>> GetByStorefrontPeriodoAsync(
        Guid storefrontId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken ct = default);
    Task AddAsync(BloqueioEntrega bloqueio, CancellationToken ct = default);
}
