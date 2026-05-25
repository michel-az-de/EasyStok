using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.InteligenciaLojas;

public class InteligenciaLojaDetalheViewModel
{
    public LojaResumoInteligenciaApi Resumo { get; set; } = new();
    public List<ProdutoTurnoverApi> TopProdutos { get; set; } = [];
    public List<ProdutoTurnoverApi> BottomProdutos { get; set; } = [];
    public List<ValidadeAlerta> AlertasValidade { get; set; } = [];
    public List<ReposicaoSugerida> Reposicoes { get; set; } = [];
    public List<IndicadorAcaoApi> Indicadores { get; set; } = [];
    public int PeriodoDias { get; set; } = 30;
}
