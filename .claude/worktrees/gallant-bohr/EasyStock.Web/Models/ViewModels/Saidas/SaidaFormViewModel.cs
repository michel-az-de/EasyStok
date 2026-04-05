using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Saidas;

public class SaidaFormViewModel
{
    public string? ProdutoId { get; set; }
    public string? VarId { get; set; }

    [Required(ErrorMessage = "Natureza é obrigatória")]
    public string Natureza { get; set; } = "venda";

    [Required(ErrorMessage = "Quantidade é obrigatória")]
    [Range(1, int.MaxValue, ErrorMessage = "Quantidade deve ser maior que zero")]
    public int Qty { get; set; }

    public decimal? Valor { get; set; }

    [Required(ErrorMessage = "Data é obrigatória")]
    public DateOnly DtVenda { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? DtSaida { get; set; }
    public DateOnly? DtEnvio { get; set; }
    public string? NotaFiscal { get; set; }
    public string? Canal { get; set; }
    public string? Descricao { get; set; }

    // Preenchidos pela view
    public Produto? ProdutoSelecionado { get; set; }
    public int QtyDisponivel { get; set; }
}
