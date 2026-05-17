using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Kds;

/// <summary>
/// View-model do Kitchen Display System (KDS).
/// Pedidos abertos agrupados por status para exibição em cards grandes.
/// </summary>
public class KdsViewModel
{
    public List<Pedido> Aguardando { get; set; } = [];
    public List<Pedido> Preparando { get; set; } = [];
    public List<Pedido> Pronto { get; set; } = [];

    public int TotalAbertos => Aguardando.Count + Preparando.Count + Pronto.Count;
}
