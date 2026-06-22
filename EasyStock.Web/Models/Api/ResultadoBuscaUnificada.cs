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
    // numeric(18,3) na API (itens_estoque.QuantidadeAtual / movimentacoes_estoque.Quantidade):
    // o JSON vem com escala (ex.: 25.000). Tinha que ser decimal? — int? quebrava a
    // desserializacao do List<> inteiro -> PARSE_ERROR -> busca global vazia (QA v1.10 BUG-01).
    public decimal? QuantidadeAtual { get; init; }
    public string? Status { get; init; }
    public string? FornecedorNome { get; init; }
    public string? Loja { get; init; }
}
