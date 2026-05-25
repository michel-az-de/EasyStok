using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class JanelaEntregaRepository(EasyStockDbContext db) : IJanelaEntregaRepository
{
    public Task<JanelaEntrega?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.JanelasEntrega.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<JanelaEntrega>> GetAtivasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.JanelasEntrega
            .AsNoTracking()
            .Where(j => j.StorefrontId == storefrontId && j.Ativa)
            .OrderBy(j => j.DiaDaSemana)
            .ThenBy(j => j.HoraInicio)
            .ToListAsync(ct);

    public Task AddAsync(JanelaEntrega janela, CancellationToken ct = default)
    {
        db.JanelasEntrega.Add(janela);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(JanelaEntrega janela, CancellationToken ct = default)
    {
        db.JanelasEntrega.Update(janela);
        return Task.CompletedTask;
    }
}
