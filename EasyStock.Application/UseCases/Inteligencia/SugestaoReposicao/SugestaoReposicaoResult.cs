namespace EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;

public sealed record SugestaoReposicaoResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    decimal QuantidadeAtual,
    int LimiteMinimo,
    decimal QuantidadeSugerida,
    decimal CustoEstimado);
