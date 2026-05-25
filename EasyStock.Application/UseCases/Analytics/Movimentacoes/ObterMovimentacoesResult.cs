using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Analytics.Movimentacoes;

public sealed record ObterMovimentacoesResult(
    int Ano,
    int Mes,
    int Dia,
    TipoMovimentacaoEstoque Tipo,
    int TotalMovimentacoes,
    int QuantidadeTotal,
    decimal ValorTotal)
{
    public static ObterMovimentacoesResult FromDto(MovimentacaoResumo dto) =>
        new(
            dto.Ano,
            dto.Mes,
            dto.Dia,
            dto.Tipo,
            dto.TotalMovimentacoes,
            dto.QuantidadeTotal,
            dto.ValorTotal);
}
