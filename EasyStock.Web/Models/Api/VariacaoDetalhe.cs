namespace EasyStock.Web.Models.Api;

public record VariacaoDetalhe
{
    public Guid VariacaoId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Cor { get; init; }
    public string? Tamanho { get; init; }
    public string? DescricaoComercial { get; init; }
    public string? CodigoBarras { get; init; }
    public string? Sku { get; init; }
    public bool Ativa { get; init; }
    public int QuantidadeEmEstoque { get; init; }
    public DateTime? UltimaEntradaEm { get; init; }
}
