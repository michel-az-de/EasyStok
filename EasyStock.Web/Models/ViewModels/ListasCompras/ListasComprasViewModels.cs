using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.ListasCompras;

public class ListasComprasIndexViewModel
{
    public List<ListaCompras> Listas { get; set; } = [];
    public string? FiltroStatus { get; set; }
}

public class ListaComprasDetailViewModel
{
    public required ListaComprasDetalhe Detalhe { get; set; }
}
