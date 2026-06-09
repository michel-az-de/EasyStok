namespace EasyStock.Domain.Specifications;

/// <summary>
/// Specification que avalia se um <see cref="ItemEstoque"/> está vencido em
/// relação a uma data de referência. Usado indiretamente por
/// <see cref="ItemEstoqueDisponivelParaSaidaSpecification"/> para bloquear
/// saídas de lotes expirados.
/// </summary>
public sealed class ItemEstoqueVencidoSpecification : IEspecificacao<ItemEstoque>
{
    private readonly DateOnly _dataReferencia;

    public ItemEstoqueVencidoSpecification(DateOnly dataReferencia)
    {
        _dataReferencia = dataReferencia;
    }

    public bool EhSatisfeitaPor(ItemEstoque item)
        => item.ValidadeEm != null &&
           DateOnly.FromDateTime(item.ValidadeEm.DataValidade) < _dataReferencia;
}
