namespace EasyStock.Web.Models.Api;

/// <summary>Resultado da venda balcao (espelha FinalizarVendaBalcaoResult da Api).</summary>
public class BalcaoResultApi
{
    public Guid PedidoId { get; set; }
    public bool Pago { get; set; }
    public decimal Total { get; set; }
    public string? FormaPagamento { get; set; }
}
