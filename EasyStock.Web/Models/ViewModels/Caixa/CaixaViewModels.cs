using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Caixa;

public class CaixaOperacaoViewModel
{
    public CaixaDia? Dia { get; set; }
    public DateOnly DataSelecionada { get; set; }
}

public class CaixaHistoricoViewModel
{
    public List<FechamentoCaixa> Fechamentos { get; set; } = [];
}

public class CaixaMobileViewModel
{
    public List<MobileCashSummary> Items { get; set; } = [];
    public bool PendingOnly { get; set; }
}
