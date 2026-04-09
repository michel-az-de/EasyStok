namespace EasyStock.Web.Models.Api;

public record Movimentacao
{
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public string? ProdutoVariacaoId { get; init; }
    public string? VendaId { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public string Natureza { get; init; } = string.Empty;
    public QuantidadeDto? Quantidade { get; init; }
    public DinheiroDto? ValorUnitario { get; init; }
    public DinheiroDto? ValorTotal { get; init; }
    public DateTime DataMovimentacao { get; init; }
    public string? Descricao { get; init; }
    public string? DocumentoReferencia { get; init; }
    public Produto? Produto { get; init; }
    public Variacao? ProdutoVariacao { get; init; }

    // Computed view helpers
    public int Qty => Quantidade?.Value ?? 0;
    public decimal? Custo => ValorUnitario?.Valor;
    public DateOnly Data => DateOnly.FromDateTime(DataMovimentacao);
}

public record QuantidadeDto
{
    public int Value { get; init; }
}
