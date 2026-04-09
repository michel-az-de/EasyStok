using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Nome é obrigatório")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "SKU é obrigatório")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Categoria é obrigatória")]
    public string Categoria { get; set; } = string.Empty;

    public string? Subcategoria { get; set; }

    [Required(ErrorMessage = "Preço é obrigatório")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Preço deve ser maior que zero")]
    public decimal Preco { get; set; }

    public decimal? Custo { get; set; }
    public int? Peso { get; set; }
    public string? Descricao { get; set; }
    public string? Emoji { get; set; }

    // Variações — enviadas como lista de nomes
    public List<string> Variacoes { get; set; } = [];
}
