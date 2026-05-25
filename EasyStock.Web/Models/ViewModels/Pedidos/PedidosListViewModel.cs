using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Pedidos;

public class PedidosListViewModel
{
    public List<Pedido> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? FiltroStatus { get; set; }
    public List<Cliente> Clientes { get; set; } = [];
    public List<CategoriaApi> Categorias { get; set; } = [];
}

public class PedidoDetailViewModel
{
    public required PedidoDetalhe Detalhe { get; set; }
}

public class PedidosMobileViewModel
{
    public List<MobilePedidoSummary> Items { get; set; } = [];
    public List<Pedido> ErpPedidos { get; set; } = [];
    public bool PendingOnly { get; set; }
}
