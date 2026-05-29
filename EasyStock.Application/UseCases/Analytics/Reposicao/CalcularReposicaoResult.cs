namespace EasyStock.Application.UseCases.Analytics.Reposicao;

public sealed record CalcularReposicaoResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    int QuantidadeAtual,
    int QuantidadeMinima,
    int QuantidadeSugeridaReposicao,
    decimal VelocidadeSaidaDiaria,
    int? DiasAteRuptura,
    decimal CustoEstimadoReposicao)
{
    public static CalcularReposicaoResult FromDto(ReposicaoSugerida dto) =>
        new(
            dto.ItemEstoqueId,
            dto.ProdutoId,
            dto.NomeProduto,
            dto.CodigoInterno,
            dto.QuantidadeAtual,
            dto.QuantidadeMinima,
            dto.QuantidadeSugeridaReposicao,
            dto.VelocidadeSaidaDiaria,
            dto.DiasAteRuptura,
            dto.CustoEstimadoReposicao);
}
