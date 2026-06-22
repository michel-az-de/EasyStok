using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Models.Api;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EasyStock.Web.Models.ViewModels.Produtos;

public class ProdutoFormViewModel
{
    public string? Id { get; set; }

    // BUG-12: limite espelha a coluna (HasMaxLength(180)) e o command (CadastrarProdutoCommand.Nome).
    // Antes so o backend barrava >180 (erro tardio); o guard de tags HTML ja existe no use case.
    [Required(ErrorMessage = "Nome é obrigatório")]
    [MaxLength(180, ErrorMessage = "Nome não pode passar de 180 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    public string? SkuBase { get; set; }

    [Required(ErrorMessage = "Categoria é obrigatória")]
    public Guid CategoriaId { get; set; }

    public Guid? SubcategoriaId { get; set; }

    public string? DescricaoBase { get; set; }

    public string? Marca { get; set; }

    public string? CodigoBarras { get; set; }

    public bool ControlaValidade { get; set; }

    public decimal? PrecoReferencia { get; set; }

    public decimal? CustoReferencia { get; set; }

    public decimal? MargemEstimada { get; set; }

    // Overrides hierarquicos de limiar (null = herdar da categoria/loja/default).
    [Range(0, 9999, ErrorMessage = "Quantidade minima precisa estar entre 0 e 9999")]
    public int? QuantidadeMinima { get; set; }

    [Range(0, 9999, ErrorMessage = "Quantidade critica precisa estar entre 0 e 9999")]
    public int? QuantidadeCritica { get; set; }

    // TipoProduto: 0 = Fisico, 1 = Alimento, 2 = Servico
    public int Tipo { get; set; } = 0;

    // C2 (RDC 727/2022): "Avulso" (default) ou "Embalado".
    // Embalado exige peso por unidade nas etiquetas (Lotes).
    // string em vez de enum porque EasyStock.Web nao referencia EasyStock.Domain
    // — o API serializa Produto.TipoEmbalagem como string (HasConversion<string>).
    public string TipoEmbalagem { get; set; } = "Avulso";

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

    // Auditoria (motivo e observação — usados na edição)
    [MaxLength(100)]
    public string? Motivo { get; set; }

    [MaxLength(500)]
    public string? Observacao { get; set; }

    [MaxLength(1000)]
    public string? ObservacaoInterna { get; set; }

    // Fotos existentes (populated on edit, not bound from form)
    [BindNever]
    public List<ProdutoFotoDetalhe> ExistingPhotos { get; set; } = [];
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
