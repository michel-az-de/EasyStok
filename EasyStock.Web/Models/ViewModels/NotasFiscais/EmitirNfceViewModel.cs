using EasyStock.Web.Services;

namespace EasyStock.Web.Models.ViewModels.NotasFiscais;

public class EmitirNfceViewModel
{
    public List<PedidoElegivelItem> PedidosElegiveis { get; set; } = new();
    public Guid? PedidoIdSelecionado { get; set; }
    public string? DestinatarioCpf { get; set; }
    public string? DestinatarioNome { get; set; }
    public string? DestinatarioEmail { get; set; }
}
