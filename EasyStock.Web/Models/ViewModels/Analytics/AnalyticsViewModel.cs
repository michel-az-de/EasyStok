using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Analytics;

public class AnalyticsViewModel
{
    // KPIs do período (vindos do dashboard summary)
    public decimal ReceitaEstimadaPeriodo { get; set; }
    public int TotalEstoque { get; set; }
    public decimal ValorEstoque { get; set; }
    public decimal MediaVendasDiaria { get; set; }

    // Dados reais do último mês (vindos do endpoint receita)
    public int UnidadesVendidasMes { get; set; }
    public decimal ReceitaMes { get; set; }

    // Projeções derivadas da velocidade média diária
    public decimal ProjUnidadesDia { get; set; }
    public decimal ProjUnidades7d { get; set; }
    public decimal ProjUnidades30d { get; set; }
    public decimal ProjReceita30d { get; set; }
    public bool ReceitaProjetadaDisponivel { get; set; }

    // Gráfico de receita mensal
    public List<string> GraficoLabels { get; set; } = [];
    public List<decimal> GraficoDados { get; set; } = [];

    // Listas
    public List<ReposicaoSugerida> ItensReposicaoUrgente { get; set; } = [];
    public List<AlertaItem> Alertas { get; set; } = [];
}

public record AlertaItem(string Tipo, string Titulo, string Mensagem, string? ReferenciaId);
