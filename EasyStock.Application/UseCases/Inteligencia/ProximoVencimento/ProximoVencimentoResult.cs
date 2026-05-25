namespace EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;

public sealed record ProximoVencimentoResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    decimal QuantidadeAtual,
    DateTime DataVencimento,
    int DiasAteVencimento);
