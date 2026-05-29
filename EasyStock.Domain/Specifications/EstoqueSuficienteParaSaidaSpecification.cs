using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Specifications;

public class EstoqueSuficienteParaSaidaSpecification : IEspecificacao<ItemEstoque>
{
    private readonly Quantidade _quantidadeSolicitada;

    public EstoqueSuficienteParaSaidaSpecification(Quantidade quantidadeSolicitada)
    {
        _quantidadeSolicitada = quantidadeSolicitada;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
    {
        return item.QuantidadeAtual.Value >= _quantidadeSolicitada.Value;
    }
}
