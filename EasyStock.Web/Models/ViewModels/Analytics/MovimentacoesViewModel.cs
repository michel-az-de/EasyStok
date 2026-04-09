using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Shared;

namespace EasyStock.Web.Models.ViewModels.Analytics;

public class MovimentacoesViewModel
{
    public List<MovimentacaoItem> Itens { get; set; } = [];
    public PaginationViewModel Paginacao { get; set; } = new();
    public string? FiltroTipo { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFim { get; set; }
}

public class MovimentacaoItem
{
    public string Tipo { get; set; } = string.Empty;
    public string Resumo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal? Valor { get; set; }
    public DateOnly Data { get; set; }
}
