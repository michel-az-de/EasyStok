using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// #3294: os indicadores da tela Pedidos de Cadastros. Somas e contagens rodam no banco, sobre os mesmos filtros da
/// lista (<see cref="PedidoFiltroDaLista"/>), sem a situação. Toda consulta, inclusive as subconsultas da primeira
/// compra e da ordem da compra, começa pela empresa do filtro.
/// </summary>
public sealed class PedidosResumoQuery(EasyStockDbContext db) : IPedidosResumoQuery
{
    /// <summary>Na operação: pago ou aprovado e ainda não entregue (as colunas da Cozinha).</summary>
    private static readonly string[] EmAndamento =
    [
        StatusPedidoMapper.Aguardando, StatusPedidoMapper.AprovadoBaba, StatusPedidoMapper.Preparando,
        StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega,
    ];

    public const int ItensMaisVendidos = 3;

    private IQueryable<Pedido> DaEmpresa(Guid empresaId) => db.Pedidos.AsNoTracking().Where(p => p.EmpresaId == empresaId);

    private IQueryable<Pedido> Filtrados(FiltroDosPedidos f) =>
        PedidoFiltroDaLista.Aplicar(DaEmpresa(f.EmpresaId), status: null, f.ClienteId, f.Desde, f.Ate, f.Search, f.EntregaDesde, f.EntregaAte);

    public async Task<ResumoDosPedidos> ResumirAsync(FiltroDosPedidos f, DateTime agoraUtc, CancellationToken ct = default)
    {
        var todos = Filtrados(f);
        var porSituacao = await todos.GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Pedidos = g.Count(), Valor = g.Sum(p => (decimal)p.Total) })
            .ToListAsync(ct);
        int Contar(Func<string, bool> regra) => porSituacao.Where(s => regra(s.Status)).Sum(s => s.Pedidos);
        decimal Somar(Func<string, bool> regra) => porSituacao.Where(s => regra(s.Status)).Sum(s => s.Valor);

        var compras = todos.Where(RegraDeCompra.Pedido);
        // Falta receber pela regra do pedido (Pedido.AReceberPelosPagamentos), só no que conta como compra. Difere do
        // "a receber" do Fluxo de caixa, que tira o pedido com conta a receber aberta.
        var saldos = compras.Where(p => p.QuitadoHistoricoEm == null)
            .Select(p => (decimal)p.Total - (p.Pagamentos.Sum(g => (decimal?)g.Valor) ?? 0m))
            .Where(saldo => saldo > 0m);
        var aReceberValor = await saldos.SumAsync(s => (decimal?)s, ct) ?? 0m;
        var aReceberPedidos = await saldos.CountAsync(ct);
        var atrasados = await todos.Where(RegraDeAtraso.Pedido(agoraUtc)).CountAsync(ct);

        // Primeira compra: nenhuma compra do mesmo cliente, na mesma empresa, antes desta. Pedido sem cliente fica fora.
        var comprasDaEmpresa = DaEmpresa(f.EmpresaId).Where(RegraDeCompra.Pedido);
        var comCliente = compras.Where(p => p.ClienteId != null);
        var comprasComCliente = await comCliente.CountAsync(ct);
        var primeiraCompra = await comCliente
            .CountAsync(p => !comprasDaEmpresa.Any(q => q.ClienteId == p.ClienteId && q.CriadoEm < p.CriadoEm), ct);

        var maisVendidos = await compras.SelectMany(p => p.Itens)
            .Where(i => !(i.CardapioItemId == null && i.ProdutoId == null && i.LinhaSnapshot == null && i.Nome.StartsWith(PedidoItem.PrefixoDoFrete)))
            .GroupBy(i => i.Nome)
            .Select(g => new { Nome = g.Key, Quantidade = g.Sum(i => i.Quantidade) })
            .OrderByDescending(g => g.Quantidade).ThenBy(g => g.Nome)
            .Take(ItensMaisVendidos)
            .ToListAsync(ct);

        return new ResumoDosPedidos(
            Pedidos: Contar(_ => true),
            Vendas: Contar(RegraDeCompra.EhCompra),
            Vendido: Somar(RegraDeCompra.EhCompra),
            AReceberValor: aReceberValor,
            AReceberPedidos: aReceberPedidos,
            AguardandoPagamento: Contar(s => s == StatusPedidoMapper.AguardandoPagamento),
            AguardandoAprovacao: Contar(s => s == StatusPedidoMapper.AguardandoAprovacaoBaba),
            EmAndamento: Contar(EmAndamento.Contains),
            Atrasados: atrasados,
            CanceladosPedidos: Contar(s => s == StatusPedidoMapper.Cancelado),
            CanceladosValor: Somar(s => s == StatusPedidoMapper.Cancelado),
            PrimeiraCompra: primeiraCompra,
            Recompra: comprasComCliente - primeiraCompra,
            MaisVendidos: maisVendidos.Select(i => new ItemMaisVendido(i.Nome, i.Quantidade)).ToList());
    }

    public async Task<VendasDoPeriodo> VendasAsync(FiltroDosPedidos f, CancellationToken ct = default)
    {
        var compras = Filtrados(f).Where(RegraDeCompra.Pedido);
        var pedidos = await compras.CountAsync(ct);
        var vendido = await compras.SumAsync(p => (decimal?)(decimal)p.Total, ct) ?? 0m;
        return new VendasDoPeriodo(pedidos, vendido);
    }

    public async Task<IReadOnlyDictionary<Guid, SinaisDoPedido>> SinaisAsync(Guid empresaId, IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct = default)
    {
        var ids = pedidoIds.Distinct().ToList();
        var daPagina = await DaEmpresa(empresaId).Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.ClienteId, p.Status, p.CriadoEm,
                // Desde quando está na situação: a última passagem para ela. Sem evento, vale a criação.
                SituacaoDesde = p.Eventos.Where(e => e.StatusNovo == p.Status).Max(e => (DateTime?)e.OcorridoEm),
            })
            .ToListAsync(ct);

        // A ordem da compra: 1 + compras anteriores do mesmo cliente nesta empresa (CriadoEm, depois Id). É a mesma
        // regra de compra que a tela Clientes conta, então a última compra de alguém tem o número das compras dele.
        var clientes = daPagina.Where(p => p.ClienteId != null && RegraDeCompra.EhCompra(p.Status))
            .Select(p => p.ClienteId!.Value).Distinct().ToList();
        var ordem = new Dictionary<Guid, int>();
        if (clientes.Count > 0)
        {
            var comprasDosClientes = await DaEmpresa(empresaId).Where(RegraDeCompra.Pedido)
                .Where(p => p.ClienteId != null && clientes.Contains(p.ClienteId.Value))
                .Select(p => new { p.Id, ClienteId = p.ClienteId!.Value, p.CriadoEm })
                .ToListAsync(ct);
            foreach (var doCliente in comprasDosClientes.GroupBy(c => c.ClienteId))
            {
                var n = 0;
                foreach (var compra in doCliente.OrderBy(c => c.CriadoEm).ThenBy(c => c.Id)) ordem[compra.Id] = ++n;
            }
        }

        return daPagina.ToDictionary(
            p => p.Id,
            p => new SinaisDoPedido(ordem.TryGetValue(p.Id, out var n) ? n : null, p.SituacaoDesde ?? p.CriadoEm));
    }
}
