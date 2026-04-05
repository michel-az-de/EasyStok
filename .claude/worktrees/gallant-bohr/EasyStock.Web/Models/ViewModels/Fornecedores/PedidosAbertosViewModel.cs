using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Fornecedores;

public class PedidosAbertosViewModel
{
    public List<PedidoFornecedor> Pedidos { get; set; } = [];
    public int TotalPedidos => Pedidos.Count;
}
