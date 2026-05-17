namespace EasyStock.Web.Models.ViewModels.Estoque;

public class QuickSaidaRequest
{
    public string? EstoqueId { get; set; }
    public string? Natureza { get; set; }
    public int Qty { get; set; }
    public decimal? Valor { get; set; }
    public string? Data { get; set; }
    public string? Motivo { get; set; }
}
