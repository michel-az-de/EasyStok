using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// Repo de <see cref="Pedido"/> no contexto Storefront — sem escopo por EmpresaId,
/// necessário para o background service varrer pedidos abandonados (ADR-0014).
/// </summary>
public sealed class PedidoStorefrontRepository(EasyStockDbContext db) : IPedidoStorefrontRepository
{
    public Task<Pedido?> GetByIdAsync(Guid pedidoId, CancellationToken ct = default) =>
        db.Pedidos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

    public async Task AddAsync(Pedido pedido, CancellationToken ct = default)
    {
        await db.Pedidos.AddAsync(pedido, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Pedido pedido, CancellationToken ct = default)
    {
        db.Pedidos.Update(pedido);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddItemAsync(PedidoItem item, CancellationToken ct = default)
    {
        await db.Set<PedidoItem>().AddAsync(item, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<Pedido>> GetAguardandoPagamentoExpiradosAsync(
        DateTime criadoAntesDe,
        int maxBatch = 50,
        CancellationToken ct = default) =>
        db.Pedidos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Status == StatusPedidoMapper.AguardandoPagamento
                     && p.Origem == "storefront"
                     && p.CriadoEm < criadoAntesDe)
            .OrderBy(p => p.CriadoEm)
            .Take(maxBatch)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<Pedido>>(t => t.Result, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public Task<IReadOnlyList<Pedido>> GetEntreguesElegiveisPraAvaliacaoAsync(
        DateTime entregueAntesDe,
        int maxBatch = 50,
        CancellationToken ct = default) =>
        db.Pedidos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Status == StatusPedidoMapper.Entregue
                     && p.Origem == "storefront"
                     && p.EntreguEm <= entregueAntesDe
                     && p.AvaliacaoSolicitadaEm == null)
            .OrderBy(p => p.EntreguEm)
            .Take(maxBatch)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<Pedido>>(t => t.Result, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
