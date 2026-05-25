namespace EasyStock.Web.Models.ViewModels.Produtos;

// Espelha ProdutoFichaTecnicaCommand do Application + omite EmpresaId/ProdutoId
// (resolvidos pelo controller a partir da sessao e da rota).
public sealed class FichaTecnicaCommand
{
    public decimal? PorcaoG { get; set; }
    public decimal? Kcal { get; set; }
    public decimal? CarbsG { get; set; }
    public decimal? ProteinaG { get; set; }
    public decimal? GorduraG { get; set; }
    public decimal? GorduraSaturadaG { get; set; }
    public decimal? FibrasG { get; set; }
    public decimal? SodioMg { get; set; }
    public string? ModoPreparo { get; set; }
    public List<string>? Ingredientes { get; set; }
    public List<string>? Alergenos { get; set; }
    public string? AlergenosOutros { get; set; }
}
