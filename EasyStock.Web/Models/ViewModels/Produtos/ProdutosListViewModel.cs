using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutosListViewModel
{
    public List<ProdutoResumo> Produtos { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();
    public string? Search { get; set; }
    public string? Categoria { get; set; }
    public string? Status { get; set; }
    public bool SemPreco { get; set; }
    public List<CategoriaApi> Categorias { get; set; } = [];
}
