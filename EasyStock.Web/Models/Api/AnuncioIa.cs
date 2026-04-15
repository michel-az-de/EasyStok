namespace EasyStock.Web.Models.Api;

public class AnuncioIaApi
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public string Titulo { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public string? InstrucoesUsadas { get; set; }
    public bool Salvo { get; set; }
    public DateTime CriadoEm { get; set; }
}
