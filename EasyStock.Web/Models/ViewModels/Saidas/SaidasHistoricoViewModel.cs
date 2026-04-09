using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Saidas;

public class SaidasHistoricoViewModel
{
    public List<Movimentacao> Itens { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();

    // KPI chips
    public int TotalRegistros { get; set; }
    public int TotalUnidades { get; set; }
    public decimal ReceitaTotal { get; set; }
    public int TotalVendas { get; set; }
    public int TotalPerdas { get; set; }

    // Filtros
    public string? FiltroNatureza { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFim { get; set; }
}
