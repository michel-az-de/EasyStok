using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// #3299: as leituras do desempenho dos produtos. Somas no banco; a venda é o pedido da empresa que conta como
/// compra (<see cref="RegraDeCompra"/>) e o produto também tem de ser da empresa, porque <c>pedido_itens.ProdutoId</c>
/// não tem guarda de empresa. A linha de frete fica fora por definição: ela não tem produto.
/// </summary>
public sealed class ProdutosDesempenhoQuery(EasyStockDbContext db) : IProdutosDesempenhoQuery
{
    public async Task<IReadOnlyList<ProdutoDoCatalogo>> CatalogoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var produtos = await db.Produtos.AsNoTracking().Where(p => p.EmpresaId == empresaId)
            .Select(p => new { p.Id, p.Nome, p.Status, p.CriadoEm, p.EhInsumo, p.EhEmbalagem, p.PrecoReferencia, p.CustoReferencia })
            .ToListAsync(ct);
        return produtos.Select(p => new ProdutoDoCatalogo(p.Id, p.Nome, p.Status == StatusProduto.Ativo, p.CriadoEm,
            DeVenda: !p.EhInsumo && !p.EhEmbalagem, p.PrecoReferencia?.Valor, p.CustoReferencia?.Valor)).ToList();
    }

    public async Task<IReadOnlyList<VendaDoProdutoNoPedido>> VendasDesdeAsync(Guid empresaId, DateTime desdeUtc, CancellationToken ct = default) =>
        await ItensVendidos(empresaId).Where(x => x.VendidoEm >= desdeUtc)
            .GroupBy(x => new { x.ProdutoId, x.PedidoId, x.VendidoEm })
            .Select(g => new VendaDoProdutoNoPedido(g.Key.ProdutoId, g.Key.PedidoId, g.Key.VendidoEm, g.Sum(x => x.Receita), g.Sum(x => x.Unidades)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HistoricoDoProduto>> HistoricoAsync(Guid empresaId, CancellationToken ct = default) =>
        await ItensVendidos(empresaId)
            .GroupBy(x => x.ProdutoId)
            .Select(g => new HistoricoDoProduto(g.Key, g.Select(x => x.PedidoId).Distinct().Count(), g.Min(x => x.VendidoEm), g.Max(x => x.VendidoEm)))
            .ToListAsync(ct);

    public async Task<decimal> ReceitaSemProdutoDesdeAsync(Guid empresaId, DateTime desdeUtc, CancellationToken ct = default) =>
        await Compras(empresaId).Where(p => p.CriadoEm >= desdeUtc)
            .SelectMany(p => p.Itens)
            // Sem produto e não é frete (PedidoItem.EhFrete, traduzível): item avulso ou prato só do cardápio.
            .Where(i => i.ProdutoId == null && !(i.CardapioItemId == null && i.LinhaSnapshot == null && i.Nome.StartsWith(PedidoItem.PrefixoDoFrete)))
            .SumAsync(i => (decimal?)i.Subtotal, ct) ?? 0m;

    private IQueryable<Pedido> Compras(Guid empresaId) =>
        db.Pedidos.AsNoTracking().Where(p => p.EmpresaId == empresaId).Where(RegraDeCompra.Pedido);

    private IQueryable<ItemVendido> ItensVendidos(Guid empresaId)
    {
        var produtosDaEmpresa = db.Produtos.Where(p => p.EmpresaId == empresaId).Select(p => p.Id);
        return Compras(empresaId)
            .SelectMany(p => p.Itens, (p, i) => new { Pedido = p, Item = i })
            .Where(x => x.Item.ProdutoId != null && produtosDaEmpresa.Contains(x.Item.ProdutoId.Value))
            .Select(x => new ItemVendido { ProdutoId = x.Item.ProdutoId!.Value, PedidoId = x.Pedido.Id, VendidoEm = x.Pedido.CriadoEm, Receita = x.Item.Subtotal, Unidades = x.Item.Quantidade });
    }

    private sealed class ItemVendido
    {
        public Guid ProdutoId { get; init; }
        public Guid PedidoId { get; init; }
        public DateTime VendidoEm { get; init; }
        public decimal Receita { get; init; }
        public decimal Unidades { get; init; }
    }
}
