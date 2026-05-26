using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IStorefrontRepository
{
    Task<StorefrontEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<StorefrontEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<StorefrontEntity?> GetByDominioCustomAsync(string dominioCustom, CancellationToken ct = default);
    Task<StorefrontEntity?> GetByEmpresaAsync(Guid empresaId, CancellationToken ct = default);
    Task AddAsync(StorefrontEntity storefront, CancellationToken ct = default);
    Task UpdateAsync(StorefrontEntity storefront, CancellationToken ct = default);

    /// <summary>
    /// Listagem cross-tenant para o painel super-admin (TASK-EZ-ADMIN-001).
    /// Retorna página ordenada por CriadoEm desc com filtros opcionais.
    /// </summary>
    Task<(IReadOnlyList<StorefrontEntity> Itens, int Total)> ListarAdminAsync(
        int skip, int take, string? buscaSlug, bool? ativo, CancellationToken ct = default);
}
