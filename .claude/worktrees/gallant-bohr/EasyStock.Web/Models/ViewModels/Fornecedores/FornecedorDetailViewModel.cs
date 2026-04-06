using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Fornecedores;

public class FornecedorDetailViewModel
{
    public required Fornecedor Fornecedor { get; set; }
    public List<PedidoFornecedor> PedidosAbertos { get; set; } = [];
    public List<PedidoFornecedor> Historico { get; set; } = [];
    public decimal TotalGasto { get; set; }
    public int LeadRealMedio { get; set; }
}
