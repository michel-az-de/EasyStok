namespace EasyStock.Web.Models.ViewModels.Dashboard;

public class DashboardViewModel
{
    public int TotalProdutos { get; set; }
    public int QuantidadeTotalEmEstoque { get; set; }
    public decimal ValorEstoque { get; set; }
    public decimal ReceitaMes { get; set; }
    public decimal MediaVendasDiaria { get; set; }

    public int EstoqueCritico { get; set; }
    public int ProximosVencimento { get; set; }
    public int ProdutosParados { get; set; }
    public int SugestoesReposicao { get; set; }

    public List<MovimentacaoRecente> MovimentacoesRecentes { get; set; } = [];
    public List<string> GraficoLabels { get; set; } = [];
    public List<decimal> GraficoDados { get; set; } = [];

    public bool IaConfigurada { get; set; }
    public bool IaIlimitada { get; set; }
    public int GeracoesIaUsadas { get; set; }
    public int GeracoesIaLimite { get; set; }
    public int GeracoesIaPercent => IaIlimitada || GeracoesIaLimite == 0
        ? 0
        : (int)Math.Min(100, Math.Round((double)GeracoesIaUsadas / GeracoesIaLimite * 100));

    public decimal ReceitaMesAtual { get; set; }
    public decimal ReceitaMesAnterior { get; set; }
    public int VariacaoReceitaPercent =>
        ReceitaMesAnterior == 0
            ? 0
            : (int)Math.Round((double)((ReceitaMesAtual - ReceitaMesAnterior) / ReceitaMesAnterior) * 100);
    public bool TemComparativoReceita => ReceitaMesAnterior > 0 && ReceitaMesAtual > 0;

    public bool TemAlertasCriticos => EstoqueCritico > 0 || ProximosVencimento > 0;
    public bool TemQualquerAlerta =>
        EstoqueCritico > 0 || ProximosVencimento > 0 || ProdutosParados > 0 || SugestoesReposicao > 0;
}

public class MovimentacaoRecente
{
    public string Tipo { get; set; } = string.Empty;
    public int TotalMovimentacoes { get; set; }
    public int Qty { get; set; }
    public decimal? Valor { get; set; }
    public DateOnly Data { get; set; }
}
