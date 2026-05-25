using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Lotes;

public class LotesListViewModel
{
    public List<Lote> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? FiltroStatus { get; set; }
    /// <summary>C2 (R10): lotes em_producao com pelo menos 1 item embalado sem peso.</summary>
    public int PendentesPesoCount { get; set; }
    /// <summary>C2 (R10): lista de pendentes para exibir quando filtro=pendente_peso.</summary>
    public List<EasyStock.Web.Services.LotePendentePesoDto>? PendentesPeso { get; set; }
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
