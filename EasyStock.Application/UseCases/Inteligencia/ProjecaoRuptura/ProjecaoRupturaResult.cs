namespace EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;

public sealed record ProjecaoRupturaResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    decimal QuantidadeAtual,
    decimal TaxaSaidaDiaria,
    int? DiasAteRuptura,
    DateTime? DataEstimadaRuptura);
