namespace EasyStock.Application.UseCases.Analytics.Margem;

public sealed record CalcularMargemResult(
    Guid ProdutoId,
    string NomeProduto,
    decimal CustoMedio,
    decimal PrecoMedioVenda,
    decimal MargemAbsoluta,
    decimal MargemPercentual,
    decimal QuantidadeVendida)
{
    public static CalcularMargemResult FromDto(MargemPorProduto dto) =>
        new(
            dto.ProdutoId,
            dto.NomeProduto,
            dto.CustoMedio,
            dto.PrecoMedioVenda,
            dto.MargemAbsoluta,
            dto.MargemPercentual,
            dto.QuantidadeVendida);
}
