using EasyStock.Domain.Specifications;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #2318 (PROD-4): estoque do ERP que o produto do app enxerga, pela regra unica <see cref="EstoqueDaLoja"/>:
/// item sem loja pertence a qualquer loja da empresa (igual a baixa de venda); produto do app sem loja
/// enxerga a empresa toda. Divergencia, contador do painel e Reconcile somam por aqui.
/// Nao unificado de proposito: MobileSaleSyncService (item de ItemVenda, FirstOrDefault) e
/// MobileStockReconciler (lotes consumidos na baixa) ja casam "LojaId == null ||"; mexer neles alteraria a baixa.
/// </summary>
internal static class EstoqueDaLojaMobile
{
    public static IEnumerable<ItemEstoque> ItensDaLoja(IEnumerable<ItemEstoque> itens, Guid produtoId, Guid? lojaId)
    {
        var daLoja = EstoqueDaLoja.Pertence(lojaId).Compile();
        return itens.Where(i => i.ProdutoId == produtoId && daLoja(i));
    }

    public static decimal Soma(IEnumerable<ItemEstoque> itens, Guid produtoId, Guid? lojaId) =>
        ItensDaLoja(itens, produtoId, lojaId).Sum(i => i.QuantidadeAtual?.Value ?? 0m);
}
