using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Estoque;

public class EstoqueListViewModel
{
    public List<EstoqueSku> Itens { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();
    public string? Search { get; set; }
    public string? StatusFiltro { get; set; }
    public string? Categoria { get; set; }
}
