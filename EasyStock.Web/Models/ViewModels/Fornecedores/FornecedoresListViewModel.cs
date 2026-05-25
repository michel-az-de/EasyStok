using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Fornecedores;

public class FornecedoresListViewModel
{
    public List<Fornecedor> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? FiltroStatus { get; set; }
}
