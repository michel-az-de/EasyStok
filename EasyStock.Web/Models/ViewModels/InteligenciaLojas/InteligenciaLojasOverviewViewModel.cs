using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.InteligenciaLojas;

public class InteligenciaLojasOverviewViewModel
{
    public List<LojaComparacaoApi> Lojas { get; set; } = [];
    public List<IndicadorAcaoApi> Indicadores { get; set; } = [];
    public int PeriodoDias { get; set; } = 30;

    public LojaComparacaoApi? MelhorLoja => Lojas.Count > 0 ? Lojas[0] : null;
    public LojaComparacaoApi? PiorLoja => Lojas.Count > 0 ? Lojas[^1] : null;
    public decimal MediaHealthScore => Lojas.Count > 0 ? Math.Round(Lojas.Average(l => l.HealthScore), 1) : 0;
    public int TotalAlertas => Lojas.Sum(l => l.AlertasTotal);
}
