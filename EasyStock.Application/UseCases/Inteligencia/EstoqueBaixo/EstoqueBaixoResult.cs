namespace EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;

public sealed record EstoqueBaixoResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    decimal QuantidadeAtual,
    int LimiteMinimo);
