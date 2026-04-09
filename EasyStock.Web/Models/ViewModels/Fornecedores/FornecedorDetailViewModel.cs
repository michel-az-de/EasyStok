using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Fornecedores;

public class FornecedorDetailViewModel
{
    public required Fornecedor Fornecedor { get; set; }
    public List<FornecedorHistoricoItem> Historico { get; set; } = [];
    public decimal TotalGasto { get; set; }
    public decimal? LeadRealMedio { get; set; }
    public int QuantidadePedidos { get; set; }
}
