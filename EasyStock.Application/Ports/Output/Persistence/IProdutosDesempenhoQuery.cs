namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// #3299: as leituras do desempenho dos produtos (tela Produtos de Cadastros). Venda é pedido que conta como compra
/// (<c>RegraDeCompra</c>). Toda leitura parte da empresa: o pedido E o produto são dela, porque o item do pedido
/// aponta o produto sem guarda de empresa.
/// </summary>
public interface IProdutosDesempenhoQuery
{
    /// <summary>Os produtos da empresa, com o que as regras precisam.</summary>
    Task<IReadOnlyList<ProdutoDoCatalogo>> CatalogoAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>Uma linha por produto e pedido vendidos desde o instante: receita e unidades somadas no pedido.</summary>
    Task<IReadOnlyList<VendaDoProdutoNoPedido>> VendasDesdeAsync(Guid empresaId, DateTime desdeUtc, CancellationToken ct = default);

    /// <summary>Histórico completo por produto: pedidos distintos, primeira e última venda.</summary>
    Task<IReadOnlyList<HistoricoDoProduto>> HistoricoAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>Receita das linhas sem produto do cadastro (item avulso ou do cardápio), fora o frete.</summary>
    Task<decimal> ReceitaSemProdutoDesdeAsync(Guid empresaId, DateTime desdeUtc, CancellationToken ct = default);
}

/// <summary>De venda: nem insumo nem embalagem. Preço e custo vêm do cadastro (centavos).</summary>
public sealed record ProdutoDoCatalogo(Guid Id, string Nome, bool Ativo, DateTime CriadoEm, bool DeVenda, decimal? PrecoTabela, decimal? Custo);

public sealed record VendaDoProdutoNoPedido(Guid ProdutoId, Guid PedidoId, DateTime VendidoEm, decimal Receita, decimal Unidades);

public sealed record HistoricoDoProduto(Guid ProdutoId, int Pedidos, DateTime PrimeiraVendaEm, DateTime UltimaVendaEm);
