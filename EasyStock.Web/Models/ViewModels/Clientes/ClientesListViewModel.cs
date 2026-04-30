using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Clientes;

public class ClientesListViewModel
{
    public List<Cliente> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? FiltroStatus { get; set; }
}

public class ClienteDetailViewModel
{
    public required ClienteDetalhe Detalhe { get; set; }
}

public class ClientesMobileViewModel
{
    public List<MobileClienteSummary> Items { get; set; } = [];
    public List<Cliente> ErpClientes { get; set; } = [];
    public bool PendingOnly { get; set; }
}
