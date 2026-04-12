using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

public record ProdutoDetalhe
{
    public Guid ProdutoId { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid CategoriaId { get; init; }
    public Guid? SubcategoriaId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? DescricaoBase { get; init; }
    public string? Marca { get; init; }
    [JsonConverter(typeof(EnumStringOrIntConverter))]
    public int Tipo { get; init; }
    public string? SkuBase { get; init; }
    public string? CodigoBarras { get; init; }
    public bool ControlaValidade { get; init; }
    [JsonConverter(typeof(EnumStringOrIntConverter))]
    public int Status { get; init; }
    public decimal? CustoReferencia { get; init; }
    public decimal? PrecoReferencia { get; init; }
    public decimal? MargemEstimada { get; init; }
    public ProdutoDimensoesDetalhe? Dimensoes { get; init; }
    public int QuantidadeTotalEstoque { get; init; }
    public DateTime? UltimaEntradaEm { get; init; }
    public List<ProdutoFotoDetalhe> Fotos { get; init; } = [];
    public List<VariacaoDetalhe> Variacoes { get; init; } = [];
    public List<ProdutoCaracteristicaDetalhe> Caracteristicas { get; init; } = [];
    public List<ProdutoEmbalagemDetalhe> Embalagens { get; init; } = [];

    public string StatusNome => Status == 0 ? "Ativo" : "Inativo";
}

public record ProdutoDimensoesDetalhe(decimal Peso, decimal Largura, decimal Altura, decimal Comprimento);
public record ProdutoFotoDetalhe(Guid FotoId, string Url, DateTime CriadoEm);
public record ProdutoCaracteristicaDetalhe(Guid CaracteristicaId, string Nome, string? Descricao, int? QuantidadeReferencia, string? VariacaoPadrao, Guid? VariacaoId, int OrdemExibicao);
public record ProdutoEmbalagemDetalhe(Guid EmbalagemId, string Nome, string? Descricao, ProdutoDimensoesDetalhe? Dimensoes, bool Padrao);
