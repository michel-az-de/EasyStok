using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Fornecedores;

public class PedidosAbertosViewModel
{
    public List<PedidoAberto> Pedidos { get; set; } = [];
    public int TotalPedidos => Pedidos.Count;
    public decimal ValorTotal => Pedidos.Sum(p => p.ValorEstimado ?? 0);
}
