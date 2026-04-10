namespace EasyStock.Web.Models.Api;

public record ProjecaoRupturaInteligenciaApi
{
    public Guid ItemEstoqueId { get; init; }
    public Guid ProdutoId { get; init; }
    public string? CodigoInterno { get; init; }
    public decimal QuantidadeAtual { get; init; }
    public decimal TaxaSaidaDiaria { get; init; }
    public int? DiasAteRuptura { get; init; }
    public DateTime? DataEstimadaRuptura { get; init; }
}
