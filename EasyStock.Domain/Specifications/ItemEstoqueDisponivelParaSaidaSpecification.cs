using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueDisponivelParaSaidaSpecification : IEspecificacao<ItemEstoque>
{
    private readonly DateOnly _dataReferencia;

    public ItemEstoqueDisponivelParaSaidaSpecification(DateTime dataReferencia)
    {
        // Converte o instante UTC para o dia operacional de Brasilia (ADR-0032).
        _dataReferencia = OperacionalFuso.DataOperacional(dataReferencia);
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        var ativoSpec = new ItemEstoqueAtivoSpecification();
        var bloqueadoSpec = new ItemEstoqueBloqueadoSpecification();
        var vencidoSpec = new ItemEstoqueVencidoSpecification(_dataReferencia);

        return ativoSpec.EhSatisfeitaPor(item) &&
               !bloqueadoSpec.EhSatisfeitaPor(item) &&
               !vencidoSpec.EhSatisfeitaPor(item) &&
               item.QuantidadeAtual.Value > 0;
    }
}
