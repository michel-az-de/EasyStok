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

    /// <summary>Total de lotes cadastrados (incluindo qty = 0). Bate com `Paginacao.Total`.</summary>
    public int? LotesCadastrados { get; set; }

    /// <summary>Subconjunto de lotes com saldo > 0. Bate com o sub-rotulo "em X lotes com saldo" do dashboard.</summary>
    public int? LotesComSaldo { get; set; }
}
