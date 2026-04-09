namespace EasyStock.Web.Models.Api;

public record ReposicaoSugerida
{
    public required string ItemEstoqueId { get; init; }
    public required string ProdutoId { get; init; }
    public string? NomeProduto { get; init; }
    public string? CodigoInterno { get; init; }
    public int QuantidadeAtual { get; init; }
    public int QuantidadeMinima { get; init; }
    public int QuantidadeSugeridaReposicao { get; init; }
    public decimal VelocidadeSaidaDiaria { get; init; }
    public int? DiasAteRuptura { get; init; }
    public decimal CustoEstimadoReposicao { get; init; }
}
