using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Analytics.Receita;

public sealed record CalcularReceitaResult(
    int Ano,
    int Mes,
    decimal ReceitaBruta,
    int TotalVendas,
    int TotalItensVendidos,
    decimal TicketMedio)
{
    public static CalcularReceitaResult FromDto(ReceitaPorPeriodo dto) =>
        new(
            dto.Ano,
            dto.Mes,
            dto.ReceitaBruta,
            dto.TotalVendas,
            dto.TotalItensVendidos,
            dto.TicketMedio);
}
