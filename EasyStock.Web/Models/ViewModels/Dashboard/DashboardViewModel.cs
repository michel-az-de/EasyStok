namespace EasyStock.Web.Models.ViewModels.Dashboard;

public class DashboardViewModel
{
    public int TotalProdutos { get; set; }
    public int QuantidadeTotalEmEstoque { get; set; }
    public decimal ValorEstoque { get; set; }
    public decimal ReceitaMes { get; set; }

    public int EstoqueCritico { get; set; }
    public int ProximosVencimento { get; set; }
    public int ProdutosParados { get; set; }
    public int SugestoesReposicao { get; set; }

    public List<MovimentacaoRecente> MovimentacoesRecentes { get; set; } = [];
    public List<string> GraficoLabels { get; set; } = [];
    public List<decimal> GraficoDados { get; set; } = [];
}

public class MovimentacaoRecente
{
    public string Tipo { get; set; } = string.Empty;
    public int TotalMovimentacoes { get; set; }
    public int Qty { get; set; }
    public decimal? Valor { get; set; }
    public DateOnly Data { get; set; }
}
