namespace EasyStock.Web.Models.Api;

public record ResultadoBuscaUnificada
{
    public required string Tipo { get; init; }        // Produto, Variacao, ItemEstoque, Fornecedor
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public string? ProdutoVariacaoId { get; init; }
    public required string Titulo { get; init; }
    public string? Subtitulo { get; init; }
    public required string ChaveExibicao { get; init; }
    public int Score { get; init; }
    public string? Sku { get; init; }
    public int? QuantidadeAtual { get; init; }
    public string? Status { get; init; }
    public string? FornecedorNome { get; init; }
    public string? Loja { get; init; }
}
