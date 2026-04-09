namespace EasyStock.Web.Models.Api;

public record FornecedorEstatisticas
{
    public required string FornecedorId { get; init; }
    public decimal TotalGasto { get; init; }
    public int QuantidadePedidos { get; init; }
    public decimal? LeadTimeRealMedioDias { get; init; }
    public decimal FrequenciaPedidosPorMes { get; init; }
}
