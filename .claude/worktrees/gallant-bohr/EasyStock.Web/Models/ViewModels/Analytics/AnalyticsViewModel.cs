using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Analytics;

public class AnalyticsViewModel
{
    public decimal ReceitaTotal { get; set; }
    public int TotalEstoque { get; set; }
    public decimal ValorEstoque { get; set; }
    public int UnidadesVendidas { get; set; }
    public decimal VelMedia { get; set; }

    // Projeções
    public decimal ProjUnidadesDia { get; set; }
    public decimal ProjUnidades7d { get; set; }
    public decimal ProjUnidades30d { get; set; }
    public decimal ProjReceita30d { get; set; }

    // Listas
    public List<EstoqueSku> ItensReposicaoUrgente { get; set; } = [];
    public List<ProdutoSazonalidade> BoardSazonalidade { get; set; } = [];
    public List<AlertaItem> Alertas { get; set; } = [];
}

public record ProdutoSazonalidade(string ProdutoId, string Nome, string? Emoji, decimal Vel, int Pct);
public record AlertaItem(string Tipo, string Titulo, string Mensagem, string? ReferenciaId);
