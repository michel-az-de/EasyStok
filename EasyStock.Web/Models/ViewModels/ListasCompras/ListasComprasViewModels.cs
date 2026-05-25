using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.ListasCompras;

public class ListasComprasIndexViewModel
{
    public List<ListaCompras> Listas { get; set; } = [];
    public string? FiltroStatus { get; set; }
}

public class ListaComprasDetailViewModel
{
    public required ListaComprasDetalhe Detalhe { get; set; }
}

public class GerarListaViewModel
{
    public List<SugestaoReposicaoApi> Sugestoes { get; set; } = [];
    public string NomeSugerido { get; set; } = "";
}

// Form de POST da tela de geração (model binding por índice: Itens[i].*).
public class GerarListaForm
{
    public string? Nome { get; set; }
    public string? Observacoes { get; set; }
    public List<GerarListaItemForm> Itens { get; set; } = [];
}

public class GerarListaItemForm
{
    public bool Incluir { get; set; }
    public string? Texto { get; set; }
    public Guid? ProdutoId { get; set; }
    public decimal? Quantidade { get; set; }
}

public class PedidosGeradosViewModel
{
    public required string ListaId { get; set; }
    public required GerarPedidosResultApi Resultado { get; set; }
}
