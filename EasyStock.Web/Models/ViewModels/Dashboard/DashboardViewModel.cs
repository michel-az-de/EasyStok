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

    // Resumo do dia (Pulso de hoje) — vem de /analytics/dia
    public int PedidosEntreguesHoje { get; set; }
    public decimal FaturamentoHoje { get; set; }
    public decimal TicketMedioHoje { get; set; }
    public int PedidosPendentes { get; set; }
    public decimal ValorPedidosPendentes { get; set; }
    public bool CaixaAbertaHoje { get; set; }
    public bool CaixaFechadaHoje { get; set; }
    public decimal SaldoCaixaAtual { get; set; }
    public int PixRecebidosHoje { get; set; }
    public decimal ValorPixHoje { get; set; }
    public bool OnboardingCompleto { get; set; } = true;

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
}

public class MovimentacaoRecente
{
    public string Tipo { get; set; } = string.Empty;
    public int TotalMovimentacoes { get; set; }
    public int Qty { get; set; }
    public decimal? Valor { get; set; }
    public DateOnly Data { get; set; }
}
