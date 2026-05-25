namespace EasyStock.Web.Models.Api;

public record ValidadeAlerta
{
    public required string ItemEstoqueId { get; init; }
    public required string ProdutoId { get; init; }
    public string? NomeProduto { get; init; }
    public string? CodigoInterno { get; init; }
    public int QuantidadeAtual { get; init; }
    public DateTime DataValidade { get; init; }
    public int DiasAteVencimento { get; init; }
    public decimal ValorEmRisco { get; init; }
}
