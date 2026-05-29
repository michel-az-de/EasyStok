using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class PedidoAvaliacaoRepository(EasyStockDbContext db) : IPedidoAvaliacaoRepository
{
    public Task<PedidoAvaliacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.PedidoAvaliacoes.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<PedidoAvaliacao?> GetByPedidoAsync(Guid pedidoId, CancellationToken ct = default) =>
        db.PedidoAvaliacoes.FirstOrDefaultAsync(a => a.PedidoId == pedidoId, ct);

    public async Task<IReadOnlyDictionary<Guid, PedidoAvaliacao>> GetByPedidoIdsAsync(
        IReadOnlyCollection<Guid> pedidoIds,
        CancellationToken ct = default)
    {
        if (pedidoIds is null || pedidoIds.Count == 0)
            return new Dictionary<Guid, PedidoAvaliacao>();

        var ids = pedidoIds.Distinct().ToArray();
        var lista = await db.PedidoAvaliacoes
            .AsNoTracking()
            .Where(a => ids.Contains(a.PedidoId))
            .ToListAsync(ct);

        // UNIQUE(PedidoId) garantido pelo EF Config — 1 avaliação por pedido.
        return lista.ToDictionary(a => a.PedidoId);
    }

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
