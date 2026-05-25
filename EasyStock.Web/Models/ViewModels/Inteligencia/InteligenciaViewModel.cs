using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Inteligencia;

public class InteligenciaViewModel
{
    // Board KPIs
    public int QuantidadeEmEstoque { get; set; }
    public decimal ValorTotalEstoque { get; set; }
    public decimal MediaVendasDiaria { get; set; }
    public decimal ProjecaoReceitaPeriodo { get; set; }

    // Critical alert lists (top 10 each)
    public List<ItemEstoqueInteligenciaApi> EstoqueBaixo { get; set; } = [];
    public List<ItemEstoqueInteligenciaApi> ProximoVencimento { get; set; } = [];
    public List<ItemEstoqueInteligenciaApi> ItensParados { get; set; } = [];
    public List<ItemEstoqueInteligenciaApi> SugestoesReposicao { get; set; } = [];

    // Stockout projections (top 5)
    public List<ProjecaoRupturaInteligenciaApi> ProjecaoRuptura { get; set; } = [];

    public bool BoardLoadFailed { get; set; }
}
