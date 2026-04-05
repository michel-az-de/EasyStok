namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record FornecedorEstatisticasResult(
    Guid FornecedorId,
    decimal TotalGasto,
    int QuantidadePedidos,
    decimal? LeadTimeRealMedioDias,
    decimal FrequenciaPedidosPorMes);
