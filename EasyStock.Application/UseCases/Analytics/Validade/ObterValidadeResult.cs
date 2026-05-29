namespace EasyStock.Application.UseCases.Analytics.Validade;

public sealed record ObterValidadeResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    int QuantidadeAtual,
    DateTime DataValidade,
    int DiasAteVencimento,
    decimal ValorEmRisco)
{
    public static ObterValidadeResult FromDto(ValidadeAlerta dto) =>
        new(
            dto.ItemEstoqueId,
            dto.ProdutoId,
            dto.NomeProduto,
            dto.CodigoInterno,
            dto.QuantidadeAtual,
            dto.DataValidade,
            dto.DiasAteVencimento,
            dto.ValorEmRisco);
}
