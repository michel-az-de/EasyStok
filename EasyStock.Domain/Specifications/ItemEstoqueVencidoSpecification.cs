using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Specifications;

/// <summary>
/// Specification que avalia se um <see cref="ItemEstoque"/> está vencido em
/// relação a uma data de referência. Usado indiretamente por
/// <see cref="ItemEstoqueDisponivelParaSaidaSpecification"/> para bloquear
/// saídas de lotes expirados.
/// </summary>
public sealed class ItemEstoqueVencidoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly DateTime _dataReferencia;

    public ItemEstoqueVencidoSpecification(DateTime dataReferencia)
    {
        _dataReferencia = dataReferencia.Date;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
        => item.ValidadeEm?.DataValidade.Date < _dataReferencia;
}
