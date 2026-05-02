using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Analytics.VendasPorCanal;

public sealed record ObterVendasPorCanalResult(
    CanalVenda Canal,
    int TotalVendas,
    int TotalItensVendidos,
    decimal ReceitaTotal,
    decimal TicketMedio,
    decimal PercentualReceita)
{
    public static ObterVendasPorCanalResult FromDto(VendaPorCanal dto) =>
        new(
            dto.Canal,
            dto.TotalVendas,
            dto.TotalItensVendidos,
            dto.ReceitaTotal,
            dto.TicketMedio,
            dto.PercentualReceita);
}
