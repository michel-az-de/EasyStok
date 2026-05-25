using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Analytics.Parados;

public sealed record ObterParadosResult(
    Guid ItemEstoqueId,
    Guid ProdutoId,
    string? NomeProduto,
    string? CodigoInterno,
    int QuantidadeAtual,
    DateTime? UltimaMovimentacaoEm,
    int DiasSemMovimentacao,
    decimal ValorParado)
{
    public static ObterParadosResult FromDto(ItemParadoDetalhe dto) =>
        new(
            dto.ItemEstoqueId,
            dto.ProdutoId,
            dto.NomeProduto,
            dto.CodigoInterno,
            dto.QuantidadeAtual,
            dto.UltimaMovimentacaoEm,
            dto.DiasSemMovimentacao,
            dto.ValorParado);
}
