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

    public string? CodigoBarras { get; set; }

    public bool ControlaValidade { get; set; }

    public decimal? PrecoReferencia { get; set; }

    public decimal? CustoReferencia { get; set; }

    public decimal? MargemEstimada { get; set; }

    // TipoProduto: 0 = Fisico, 1 = Alimento, 2 = Servico
    public int Tipo { get; set; } = 0;

    // StatusProduto: 0 = Ativo, 1 = Inativo (only used on edit)
    public int Status { get; set; } = 0;

    // Dimensoes
    public decimal? DimensoesPeso { get; set; }
    public decimal? DimensoesLargura { get; set; }
    public decimal? DimensoesAltura { get; set; }
    public decimal? DimensoesComprimento { get; set; }

    // Variacoes ricas
    public List<VariacaoFormItem> VariacoesRich { get; set; } = [];

    // Caracteristicas
    public List<CaracteristicaFormItem> Caracteristicas { get; set; } = [];

    // Embalagens
    public List<EmbalagemFormItem> Embalagens { get; set; } = [];
}

public class VariacaoFormItem
{
    public string Nome { get; set; } = string.Empty;
    public string? Cor { get; set; }
    public string? Tamanho { get; set; }
    public string? DescricaoComercial { get; set; }
    public string? Sku { get; set; }
    public string? CodigoBarras { get; set; }
    public bool Ativa { get; set; } = true;
}

public class CaracteristicaFormItem
{
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public int? QuantidadeReferencia { get; set; }
    public string? VariacaoPadrao { get; set; }
    public int OrdemExibicao { get; set; }
}

public class EmbalagemFormItem
{
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public decimal? Peso { get; set; }
    public decimal? Largura { get; set; }
    public decimal? Altura { get; set; }
    public decimal? Comprimento { get; set; }
    public bool Padrao { get; set; }
}
