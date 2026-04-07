namespace EasyStock.Web.Models.Api;

public record Saida
{
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public required string VarId { get; init; }
    public required string Natureza { get; init; }
    public int Qty { get; init; }
    public decimal? Valor { get; init; }
    public DateOnly DtVenda { get; init; }
    public DateOnly? DtSaida { get; init; }
    public DateOnly? DtEnvio { get; init; }
    public string? NotaFiscal { get; init; }
    public string? Canal { get; init; }
    public string? Descricao { get; init; }
    public Produto? Produto { get; init; }
    public Variacao? Variacao { get; init; }
}
