using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class FreteZonaRepository(EasyStockDbContext db) : IFreteZonaRepository
{
    public Task<FreteZona?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.FreteZonas.FirstOrDefaultAsync(z => z.Id == id, ct);

    public async Task<IReadOnlyList<FreteZona>> GetAtivasDoStorefrontOrdenadasAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.FreteZonas
            .AsNoTracking()
            .Where(z => z.StorefrontId == storefrontId && z.Ativa)
            .OrderBy(z => z.Ordem)
            .ThenBy(z => z.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FreteZona>> GetTodasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.FreteZonas
            .AsNoTracking()
            .Where(z => z.StorefrontId == storefrontId)
            .OrderBy(z => z.Ordem)
            .ThenBy(z => z.Id)
            .ToListAsync(ct);

    public Task AddAsync(FreteZona zona, CancellationToken ct = default)
    {
        db.FreteZonas.Add(zona);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FreteZona zona, CancellationToken ct = default)
    {
        db.FreteZonas.Update(zona);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Implementação in-memory deliberada: cada storefront tem poucas zonas
    /// (tipicamente &lt;20) e a lógica de match (CEP range / bairros JSON) já
    /// vive em <see cref="FreteZona.CobreCep"/> / <see cref="FreteZona.CobreBairro"/>.
    /// Duplicá-la em SQL traria fragilidade (JSON contains com LIKE textual
    /// não é portável) sem ganho de performance. Quando o catálogo crescer
    /// acima de algumas centenas de zonas, otimizar com filtro CEP range
    /// SQL-side + filtro bairro client-side.
    /// </remarks>
    public async Task<FreteZona?> BuscarZonaPorCepAsync(
        Guid storefrontId,
        string cep,
        string bairroNormalizado,
        CancellationToken ct = default)
    {
        var zonas = await GetAtivasDoStorefrontOrdenadasAsync(storefrontId, ct);
        foreach (var zona in zonas)
        {
            if (zona.CobreCep(cep))
                return zona;
            if (!string.IsNullOrEmpty(bairroNormalizado) && zona.CobreBairro(bairroNormalizado))
                return zona;
        }
        return null;
    }
}
