using System.ComponentModel.DataAnnotations;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Nome é obrigatório")]
    public string Nome { get; set; } = string.Empty;

    public string? SkuBase { get; set; }

    [Required(ErrorMessage = "Categoria é obrigatória")]
    public Guid CategoriaId { get; set; }

    public string? DescricaoBase { get; set; }

    public string? Marca { get; set; }

    public decimal? PrecoReferencia { get; set; }

    public decimal? CustoReferencia { get; set; }

    // TipoProduto: 0 = Fisico, 1 = Alimento, 2 = Servico
    public int Tipo { get; set; } = 0;

    // StatusProduto: 0 = Ativo, 1 = Inativo (only used on edit)
    public int Status { get; set; } = 0;

    // Variações — enviadas como lista de nomes
    public List<string> Variacoes { get; set; } = [];
}
