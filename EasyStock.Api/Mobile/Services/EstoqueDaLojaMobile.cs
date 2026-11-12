namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #2318 (PROD-4): regra unica de "estoque do ERP que o produto do app enxerga". Divergencia, contador do
/// painel e reconciliacao usam a mesma soma por (ProdutoId, LojaId). Item sem loja nao entra na soma de
/// loja nenhuma (entrava em todas e contava em dobro); so casa com produto do app tambem sem loja.
/// </summary>
internal static class EstoqueDaLojaMobile
{
    public static IEnumerable<ItemEstoque> ItensDaLoja(IEnumerable<ItemEstoque> itens, Guid produtoId, Guid? lojaId) =>
        itens.Where(i => i.ProdutoId == produtoId && i.LojaId == lojaId);

    public static decimal Soma(IEnumerable<ItemEstoque> itens, Guid produtoId, Guid? lojaId) =>
        ItensDaLoja(itens, produtoId, lojaId).Sum(i => i.QuantidadeAtual?.Value ?? 0m);
}
