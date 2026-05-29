namespace EasyStock.Application.UseCases.Analytics.Dashboard;

public sealed record GetDashboardResult(
    Guid EmpresaId,
    int Periodo,
    int TotalSkus,
    int QuantidadeTotalEmEstoque,
    decimal ValorTotalEstoque,
    decimal ValorCustoEstoque,
    decimal MediaVendasDiaria,
    decimal ProjecaoVendasPeriodo,
    decimal ReceitaEstimadaPeriodo,
    int AlertasEstoqueBaixo,
    int AlertasVencimento,
    int AlertasItensParados)
{
    public static GetDashboardResult FromDto(DashboardResumo dto) =>
        new(
            dto.EmpresaId,
            dto.Periodo,
            dto.TotalSkus,
            dto.QuantidadeTotalEmEstoque,
            dto.ValorTotalEstoque,
            dto.ValorCustoEstoque,
            dto.MediaVendasDiaria,
            dto.ProjecaoVendasPeriodo,
            dto.ReceitaEstimadaPeriodo,
            dto.AlertasEstoqueBaixo,
            dto.AlertasVencimento,
            dto.AlertasItensParados);
}
