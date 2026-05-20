namespace EasyStock.Web.Models.Api;

// Mapeia o SugestaoReposicaoResult do endpoint inteligencia/sugestao-reposicao.
public record SugestaoReposicaoApi
{
    public Guid ItemEstoqueId { get; init; }
    public Guid ProdutoId { get; init; }
    public string? NomeProduto { get; init; }
    public string? CodigoInterno { get; init; }
    public decimal QuantidadeAtual { get; init; }
    public int LimiteMinimo { get; init; }
    public decimal QuantidadeSugerida { get; init; }
    public decimal CustoEstimado { get; init; }

    public string Nome =>
        !string.IsNullOrWhiteSpace(NomeProduto) ? NomeProduto!
        : !string.IsNullOrWhiteSpace(CodigoInterno) ? CodigoInterno!
        : ProdutoId.ToString("N")[..8].ToUpper();
}
