namespace EasyStock.Web.Models.Api;

public record Produto
{
    public required string Id { get; init; }
    public required string Sku { get; init; }
    public required string Nome { get; init; }
    public string? Emoji { get; init; }
    public required string Categoria { get; init; }
    public string? Subcategoria { get; init; }
    public string? Descricao { get; init; }
    public decimal Preco { get; init; }
    public decimal? Custo { get; init; }
    public int? Peso { get; init; }
    public DateOnly? Validade { get; init; }
    public List<string> Fotos { get; init; } = [];
    public required string Status { get; init; }
    public List<Variacao> Variacoes { get; init; } = [];
}
