namespace EasyStock.Application.UseCases.Inteligencia.ItensParados;

public sealed record ItensParadosResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    decimal QuantidadeAtual,
    DateTime UltimaMovimentacao,
    int DiasSemMovimento);
