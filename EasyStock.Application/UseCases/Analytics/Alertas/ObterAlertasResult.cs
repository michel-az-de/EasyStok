using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Analytics.Alertas;

public sealed record ObterAlertasResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    int QuantidadeAtual,
    DateTime DataValidade,
    int DiasAteVencimento,
    decimal ValorEmRisco)
{
    public static ObterAlertasResult FromDto(ValidadeAlerta dto) =>
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
