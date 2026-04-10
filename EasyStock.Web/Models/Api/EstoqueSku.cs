namespace EasyStock.Web.Models.Api;

public record EstoqueSku
{
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public required string VarId { get; init; }
    public required string Sku { get; init; }
    public int Qty { get; init; }
    public DateOnly EntryDate { get; init; }
    public DateTimeOffset LastMov { get; init; }
    public DateOnly? Validade { get; init; }
    public string? Lote { get; init; }
    public decimal Vel { get; init; }
    public int Stopped { get; init; }
    public required string Status { get; init; }
    public DinheiroDto? CustoUnitario { get; init; }
    public DinheiroDto? PrecoVendaSugerido { get; init; }
    public Produto? Produto { get; init; }
    public Variacao? Variacao { get; init; }
}
