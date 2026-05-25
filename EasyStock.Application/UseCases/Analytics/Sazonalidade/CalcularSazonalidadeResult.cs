using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Analytics.Sazonalidade;

public sealed record CalcularSazonalidadeResult(
    int Ano,
    int Mes,
    int TotalSaidas,
    decimal ValorTotal,
    decimal MediaMovelTresMeses)
{
    public static CalcularSazonalidadeResult FromDto(SazonalidadeMensal dto) =>
        new(
            dto.Ano,
            dto.Mes,
            dto.TotalSaidas,
            dto.ValorTotal,
            dto.MediaMovelTresMeses);
}
