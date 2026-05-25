using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Entradas;

public class EntradasHistoricoViewModel
{
    public List<Movimentacao> Entradas { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();
    public string? Tipo { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFim { get; set; }
}
