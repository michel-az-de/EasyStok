using EasyStock.Application.UseCases.Analytics.Dashboard;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Resposta de <c>GET api/analytics/dashboard</c>. O resumo de estoque é de toda a equipe; a receita do período é
/// relatório e sai nula para quem não tem a permissão de relatórios (#2821).
/// </summary>
public sealed record DashboardResumoResponse(
    Guid EmpresaId,
    int Periodo,
    int TotalSkus,
    int QuantidadeTotalEmEstoque,
    decimal ValorTotalEstoque,
    decimal ValorCustoEstoque,
    decimal MediaVendasDiaria,
    decimal ProjecaoVendasPeriodo,
    decimal? ReceitaEstimadaPeriodo,
    int AlertasEstoqueBaixo,
    int AlertasVencimento,
    int AlertasItensParados,
    int AlertasVencidos)
{
    public static DashboardResumoResponse De(GetDashboardResult r, bool podeVerReceita) => new(
        r.EmpresaId, r.Periodo, r.TotalSkus, r.QuantidadeTotalEmEstoque, r.ValorTotalEstoque, r.ValorCustoEstoque,
        r.MediaVendasDiaria, r.ProjecaoVendasPeriodo, podeVerReceita ? r.ReceitaEstimadaPeriodo : null,
        r.AlertasEstoqueBaixo, r.AlertasVencimento, r.AlertasItensParados, r.AlertasVencidos);
}
