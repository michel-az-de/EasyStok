using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoDetailViewModel
{
    public required Produto Produto { get; set; }
    public List<EstoqueSku> EstoqueSkus { get; set; } = [];
}
