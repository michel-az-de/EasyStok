using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.NotasFiscais;

public class NotasFiscaisListViewModel
{
    public List<NfeListItem> Itens { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();
    public string? FiltroStatus { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFim { get; set; }
    public string? Busca { get; set; }
}
