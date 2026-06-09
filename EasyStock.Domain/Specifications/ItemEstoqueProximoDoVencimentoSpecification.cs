namespace EasyStock.Domain.Specifications;

public class ItemEstoqueProximoDoVencimentoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly int _diasJanela;
    private readonly DateOnly _dataHoje;

    /// <param name="diasJanela">Janela em dias a partir de hoje.</param>
    /// <param name="dataHoje">Dia operacional de Brasilia (HorarioBrasil.Hoje() ou OperacionalFuso.DataOperacional(utcNow)).</param>
    public ItemEstoqueProximoDoVencimentoSpecification(int diasJanela, DateOnly dataHoje)
    {
        if (diasJanela < 0)
            throw new ArgumentOutOfRangeException(nameof(diasJanela), "A janela de dias nao pode ser negativa.");

        _diasJanela = diasJanela;
        _dataHoje = dataHoje;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        if (item.ValidadeEm == null) return false;

        var diasParaVencimento = item.ValidadeEm.DiasAteVencimento(_dataHoje);
        return diasParaVencimento > 0 && diasParaVencimento <= _diasJanela;
    }
}
