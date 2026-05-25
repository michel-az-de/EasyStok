namespace EasyStock.Web.Models.Api;

/// <summary>Produto inline retornado dentro de EstoqueSku e Movimentacao (apenas 6 campos mapeados pela API).</summary>
public record ProdutoResumoApi
{
    public required string Id { get; init; }
    public required string Sku { get; init; }
    public required string Nome { get; init; }
    public string? Emoji { get; init; }
    public required string Categoria { get; init; }
    public required string Status { get; init; }
}

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
