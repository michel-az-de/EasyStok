using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Helpers;

namespace EasyStock.Web.Models.ViewModels.Entradas;

public class ReposicaoFormViewModel
{
    public string? ItemEstoqueId { get; set; }
    public string? ProdutoId { get; set; }
    public string? ProdutoNome { get; set; }
    public string? VariacaoNome { get; set; }
    public int EstoqueAtual { get; set; }

    [Required(ErrorMessage = "Quantidade é obrigatória")]
    [Range(1, int.MaxValue, ErrorMessage = "Quantidade deve ser maior que zero")]
    public int Qty { get; set; }

    public decimal? Custo { get; set; }
    public decimal? Preco { get; set; }
    public string? Observacoes { get; set; }
    public DateOnly? Validade { get; set; }

    [Required(ErrorMessage = "Data é obrigatória")]
    public DateOnly Data { get; set; } = BrazilTime.Today();
}
