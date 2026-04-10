namespace EasyStock.Web.Models.Api;

public record ItemEstoqueInteligenciaApi
{
    public Guid Id { get; init; }
    public Guid ProdutoId { get; init; }
    public string? CodigoInterno { get; init; }
    public QuantidadeValueApi QuantidadeAtual { get; init; } = new();
    public int QuantidadeMinima { get; init; }
    public decimal VelocidadeSaidaDiaria { get; init; }
    public int DiasSemMovimentacao { get; init; }
    public int? PrevisaoZeramentoDias { get; init; }
    public DinheiroValueApi CustoUnitario { get; init; } = new();
    public ValidadeValueApi? ValidadeEm { get; init; }
}

public record QuantidadeValueApi { public decimal Value { get; init; } }
public record DinheiroValueApi   { public decimal Valor { get; init; } }
public record ValidadeValueApi   { public DateTime DataValidade { get; init; } }
