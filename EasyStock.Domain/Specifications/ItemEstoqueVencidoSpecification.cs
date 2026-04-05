using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueVencidoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly DateTime _dataReferencia;

    public ItemEstoqueVencidoSpecification(DateTime dataReferencia)
    {
        _dataReferencia = dataReferencia.Date;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        return item.ValidadeEm?.DataValidade.Date < _dataReferencia;
    }
}
