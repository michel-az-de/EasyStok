namespace EasyStock.Web.Models.Api;

public record DashboardResumoApi
{
    public Guid EmpresaId { get; init; }
    public int Periodo { get; init; }
    public int TotalSkus { get; init; }
    public int QuantidadeTotalEmEstoque { get; init; }
    public decimal ValorTotalEstoque { get; init; }
    public decimal ValorCustoEstoque { get; init; }
    public decimal MediaVendasDiaria { get; init; }
    public decimal ProjecaoVendasPeriodo { get; init; }
    public decimal ReceitaEstimadaPeriodo { get; init; }
    public int AlertasEstoqueBaixo { get; init; }
    public int AlertasVencimento { get; init; }
    public int AlertasItensParados { get; init; }
}
