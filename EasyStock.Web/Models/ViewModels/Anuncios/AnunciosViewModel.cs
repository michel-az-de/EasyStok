using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Anuncios;

public class AnunciosViewModel
{
    public List<Produto> Produtos { get; set; } = [];
}

public class GerarAnuncioRequest
{
    public string? ProdutoId { get; set; }
    public string? VarId { get; set; }
    public string Canal { get; set; } = "ML";
    public string Tom { get; set; } = "profissional";
    public string Foco { get; set; } = "beneficios";
    public string? Contexto { get; set; }
}
