using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueBloqueadoSpecification : IEspecificacao<ItemEstoque>
{
    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        return item.Status == StatusItemEstoque.Bloqueado;
    }
}
