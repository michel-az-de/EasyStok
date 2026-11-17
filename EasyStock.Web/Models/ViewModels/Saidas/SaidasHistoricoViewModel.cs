using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Saidas;

public class SaidasHistoricoViewModel
{
    public List<Movimentacao> Itens { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();

    // KPI chips
    public int TotalRegistros { get; set; }
    public decimal TotalUnidades { get; set; }
    /// <summary>#2821: nula quando a API omite a receita a quem não tem a permissão de relatórios.</summary>
    public decimal? ReceitaTotal { get; set; } = 0m;
    public int TotalVendas { get; set; }
    public int TotalPerdas { get; set; }

    // Filtros
    public string? FiltroNatureza { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFim { get; set; }
}
