using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueDisponivelParaSaidaSpecification : IEspecificacao<ItemEstoque>
{
    private readonly DateTime _dataReferencia;

    public ItemEstoqueDisponivelParaSaidaSpecification(DateTime dataReferencia)
    {
        _dataReferencia = dataReferencia;
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
