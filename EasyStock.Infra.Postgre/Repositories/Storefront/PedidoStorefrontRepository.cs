using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

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

    /// <summary>
    /// SELECT FOR UPDATE — lock pessimista no Pedido. ADR-0014 (vaga lifecycle) + TASK-EZ-APROVAR-001.
    /// Falha rápido (InvalidOperationException) se chamado fora de transação ativa para evitar
    /// que o lock seja descartado silenciosamente — causando race condition que destrói a serialização.
    /// </summary>
    public async Task<Pedido?> GetForUpdateAsync(Guid pedidoId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "GetForUpdateAsync deve ser chamado dentro de IUnitOfWork.ExecuteInTransactionAsync — " +
                "sem transação ativa, o FOR UPDATE é descartado pelo Postgres e a serialização entre " +
                "agentes Babá fica quebrada (race em aprovar/recusar concorrente).");

        const string sql = "SELECT * FROM pedidos WHERE \"Id\" = {0} FOR UPDATE";
        return await db.Pedidos
            .FromSqlRaw(sql, pedidoId)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);
    }

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

    public async Task AddEventoAsync(PedidoEvento evento, CancellationToken ct = default)
    {
        await db.Set<PedidoEvento>().AddAsync(evento, ct);
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

    public async Task<IReadOnlyList<Pedido>> ListarPorClienteAsync(
        Guid empresaId,
        Guid clienteId,
        int limit,
        CancellationToken ct = default)
    {
        // IgnoreQueryFilters porque o filtro de tenant é aplicado manualmente
        // (EmpresaId) — alinhado com os outros métodos do repo storefront.
        var pedidos = await db.Pedidos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresaId
                     && p.ClienteId == clienteId
                     && p.Origem == "storefront"
                     && p.Status != StatusPedidoMapper.Rascunho)
            .Include(p => p.Itens)
            .OrderByDescending(p => p.CriadoEm)
            .Take(limit)
            .ToListAsync(ct);

        return pedidos;
    }

    public Task<Pedido?> ObterDoClienteAsync(
        Guid empresaId,
        Guid clienteId,
        Guid pedidoId,
        CancellationToken ct = default)
    {
        // Mesmo filtro de posse do ListarPorClienteAsync, mas por Id único.
        // IgnoreQueryFilters: tenant aplicado manualmente (EmpresaId).
        return db.Pedidos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == pedidoId
                     && p.EmpresaId == empresaId
                     && p.ClienteId == clienteId
                     && p.Origem == "storefront"
                     && p.Status != StatusPedidoMapper.Rascunho)
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(ct);
    }
}
