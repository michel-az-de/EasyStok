using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Analytics.Projecoes;

public sealed record CalcularProjecoesResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    int QuantidadeAtual,
    decimal TaxaSaidaDiaria,
    int? DiasAteRuptura,
    DateTime? DataEstimadaRuptura)
{
    public static CalcularProjecoesResult FromDto(ProjecaoRuptura dto) =>
        new(
            dto.ItemEstoqueId,
            dto.ProdutoId,
            dto.NomeProduto,
            dto.CodigoInterno,
            dto.QuantidadeAtual,
            dto.TaxaSaidaDiaria,
            dto.DiasAteRuptura,
            dto.DataEstimadaRuptura);
}
