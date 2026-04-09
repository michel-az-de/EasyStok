namespace EasyStock.Web.Models.Api;

public record Fornecedor
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public bool Ativo { get; init; }
    public string? Documento { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Contato { get; init; }
    public string? Categoria { get; init; }
    public string? Tipo { get; init; }
    public int? LeadTimeEstimadoDias { get; init; }
    public decimal? LeadTimeRealMedioDias { get; init; }
    public string? SiteUrl { get; init; }
    public string? PedidoMinimo { get; init; }
    public string? FretePadrao { get; init; }
    public string? Observacoes { get; init; }
    public string Status => Ativo ? "ativo" : "inativo";
}
