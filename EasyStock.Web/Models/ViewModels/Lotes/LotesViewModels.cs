using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Lotes;

public class LotesListViewModel
{
    public List<Lote> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? FiltroStatus { get; set; }
}

public class LoteDetailViewModel
{
    public required LoteDetalhe Detalhe { get; set; }
}

public class LotesMobileViewModel
{
    public List<MobileBatchSummary> Items { get; set; } = [];
    public List<Lote> ErpLotes { get; set; } = [];
    public bool PendingOnly { get; set; }
}
