using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueProximoDoVencimentoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly int _diasJanela;

    public ItemEstoqueProximoDoVencimentoSpecification(int diasJanela)
    {
        if (diasJanela < 0)
            throw new ArgumentOutOfRangeException(nameof(diasJanela), "A janela de dias nao pode ser negativa.");

        _diasJanela = diasJanela;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        if (item.ValidadeEm == null) return false;

        var hoje = DateTime.Today;
        var dataValidade = item.ValidadeEm.DataValidade.Date;
        var diasParaVencimento = (dataValidade - hoje).TotalDays;
        return diasParaVencimento > 0 && diasParaVencimento <= _diasJanela;
    }
}
