using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class PedidoAvaliacaoRepository(EasyStockDbContext db) : IPedidoAvaliacaoRepository
{
    public Task<PedidoAvaliacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.PedidoAvaliacoes.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<PedidoAvaliacao?> GetByPedidoAsync(Guid pedidoId, CancellationToken ct = default) =>
        db.PedidoAvaliacoes.FirstOrDefaultAsync(a => a.PedidoId == pedidoId, ct);

    public async Task<IReadOnlyList<PedidoAvaliacao>> GetVisiveisDaEmpresaAsync(
        Guid empresaId,
        int max = 50,
        CancellationToken ct = default) =>
        await db.PedidoAvaliacoes
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.OcultadoEm == null)
            .OrderByDescending(a => a.RespondidoEm)
            .Take(max)
            .ToListAsync(ct);

    public Task AddAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default)
    {
        db.PedidoAvaliacoes.Add(avaliacao);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default)
    {
        db.PedidoAvaliacoes.Update(avaliacao);
        return Task.CompletedTask;
    }
}
