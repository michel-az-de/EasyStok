using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueProximoDoVencimentoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly int _diasJanela;

    public ItemEstoqueProximoDoVencimentoSpecification(int diasJanela)
    {
        _diasJanela = diasJanela;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        if (item.ValidadeEm == null) return false;
        var diasParaVencimento = (item.ValidadeEm.DataValidade - DateTime.Now).TotalDays;
        return diasParaVencimento > 0 && diasParaVencimento <= _diasJanela;
    }
}
