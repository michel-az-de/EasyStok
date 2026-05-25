namespace EasyStock.Web.Models.Api;

public record InteligenciaBoardApi
{
    public Guid EmpresaId { get; init; }
    public int Periodo { get; init; }
    public int QuantidadeEmEstoque { get; init; }
    public decimal ValorTotalEstoque { get; init; }
    public decimal MediaVendasDiaria { get; init; }
    public decimal ProjecaoVendasPeriodo { get; init; }
    public decimal ProjecaoReceitaPeriodo { get; init; }
}
