using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Postgre.Data;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class StorefrontRepository(EasyStockDbContext db) : IStorefrontRepository
{
    public Task<StorefrontEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Storefronts.FirstOrDefaultAsync(s => s.Id == id, ct);

    // #1345: chave de entrada pública, sem tenant ainda. IgnoreQueryFilters não basta: a policy RLS
    // tenant_isolation esconde a linha sem app.empresa_id (só não esconde para superusuário, como no
    // Postgres local). Mesmo padrão de UsuarioRepository.GetByEmailAsync.
    public async Task<StorefrontEntity?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        using (db.UseRowLevelSecurityBypass())
            return await db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct);
    }

    public async Task<StorefrontEntity?> GetByDominioCustomAsync(string dominioCustom, CancellationToken ct = default)
    {
        using (db.UseRowLevelSecurityBypass())
            return await db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.DominioCustom == dominioCustom, ct);
    }

    public Task<StorefrontEntity?> GetByEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        db.Storefronts.FirstOrDefaultAsync(s => s.EmpresaId == empresaId, ct);

    public Task AddAsync(StorefrontEntity storefront, CancellationToken ct = default)
    {
        db.Storefronts.Add(storefront);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StorefrontEntity storefront, CancellationToken ct = default)
    {
        db.Storefronts.Update(storefront);
        return Task.CompletedTask;
    }
}
