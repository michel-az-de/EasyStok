using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Helpers;

namespace EasyStock.Web.Models.ViewModels.Entradas;

public class EntradaFormViewModel
{
    public string? ProdutoId { get; set; }
    public string? ProdutoNome { get; set; }
    public string? ProdutoSku { get; set; }
    public string? ProdutoEmoji { get; set; }
    public int EstoqueAtual { get; set; }

    public string? VarId { get; set; }

    [Required(ErrorMessage = "Quantidade é obrigatória")]
    [Range(1, int.MaxValue, ErrorMessage = "Quantidade deve ser maior que zero")]
    public int Qty { get; set; }

    [Required(ErrorMessage = "Custo é obrigatório")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Custo deve ser maior que zero")]
    public decimal Custo { get; set; }

    public decimal? Preco { get; set; }
    public string? FornecedorId { get; set; }
    public string? Lote { get; set; }
    public DateOnly? Validade { get; set; }
    public string? Observacoes { get; set; }

    [Required(ErrorMessage = "Data é obrigatória")]
    public DateOnly Data { get; set; } = BrazilTime.Today();
}
