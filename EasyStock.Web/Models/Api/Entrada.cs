namespace EasyStock.Web.Models.Api;

public record Entrada
{
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public required string VarId { get; init; }
    public required string Tipo { get; init; }
    public int Qty { get; init; }
    public decimal? Custo { get; init; }
    public decimal? Preco { get; init; }
    public string? FornecedorId { get; init; }
    public string? Lote { get; init; }
    public DateOnly? Validade { get; init; }
    public string? Observacoes { get; init; }
    public DateOnly Data { get; init; }
    public Produto? Produto { get; init; }
    public Variacao? Variacao { get; init; }
}
