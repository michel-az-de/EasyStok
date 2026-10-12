using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Queries;

/// <summary>Pedidos do cliente para o dossiê (S25): projeção leve, <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public sealed class HistoricoPedidosClienteQueries(EasyStockDbContext db) : IHistoricoPedidosClienteQueries
{
    public async Task<IReadOnlyList<PedidoResumoCliente>> ListarAsync(
        Guid empresaId, Guid clienteId, int maximo, CancellationToken ct = default)
    {
        var linhas = await db.Pedidos
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ClienteId == clienteId)
            .OrderByDescending(p => p.CriadoEm)
            .Take(maximo)
            .Select(p => new
            {
                p.Id,
                p.Status,
                p.CriadoEm,
                p.Total,
                Itens = p.Itens.OrderBy(i => i.CriadoEm).Select(i => new ItemPedidoResumo(i.Nome, i.Quantidade)).ToList(),
            })
            .ToListAsync(ct);

        return linhas
            .Select(l => new PedidoResumoCliente(l.Id, l.Status, l.CriadoEm, l.Total.Valor, l.Itens))
            .ToList();
    }
}
