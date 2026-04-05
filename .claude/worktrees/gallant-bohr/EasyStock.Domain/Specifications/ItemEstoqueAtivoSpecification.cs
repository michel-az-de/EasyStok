using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Specifications;

public class ItemEstoqueAtivoSpecification : IEspecificacao<ItemEstoque>
{
    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        return item.Status is StatusItemEstoque.Ok or StatusItemEstoque.Warn or StatusItemEstoque.Critical or StatusItemEstoque.Slow;
    }
}
