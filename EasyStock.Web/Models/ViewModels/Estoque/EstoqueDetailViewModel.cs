using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Estoque;

public class EstoqueDetailViewModel
{
    public required EstoqueSku Item { get; set; }
    public ProdutoResumoApi? Produto { get; set; }
    public Variacao? Variacao { get; set; }
}
