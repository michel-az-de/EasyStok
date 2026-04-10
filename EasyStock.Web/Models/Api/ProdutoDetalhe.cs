namespace EasyStock.Web.Models.Api;

public record ProdutoDetalhe
{
    public Guid ProdutoId { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid CategoriaId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? DescricaoBase { get; init; }
    public string? Marca { get; init; }
    public int Tipo { get; init; }
    public string? SkuBase { get; init; }
    public string? CodigoBarras { get; init; }
    public bool ControlaValidade { get; init; }
    public int Status { get; init; }
    public decimal? CustoReferencia { get; init; }
    public decimal? PrecoReferencia { get; init; }
    public decimal? MargemEstimada { get; init; }
    public int QuantidadeTotalEstoque { get; init; }
    public DateTime? UltimaEntradaEm { get; init; }
    public List<ProdutoFotoDetalhe> Fotos { get; init; } = [];
    public List<VariacaoDetalhe> Variacoes { get; init; } = [];
    public DimensoesDetalhe? Dimensoes { get; init; }
    public List<CaracteristicaDetalhe> Caracteristicas { get; init; } = [];
    public List<EmbalagemDetalhe> Embalagens { get; init; } = [];

    public string StatusNome => Status == 0 ? "Ativo" : "Inativo";
}

public record ProdutoFotoDetalhe(Guid FotoId, string Url, DateTime CriadoEm);

public record DimensoesDetalhe
{
    public decimal Peso { get; init; }
    public decimal Largura { get; init; }
    public decimal Altura { get; init; }
    public decimal Comprimento { get; init; }
}

public record CaracteristicaDetalhe
{
    public Guid CaracteristicaId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Descricao { get; init; }
    public int? QuantidadeReferencia { get; init; }
    public string? VariacaoPadrao { get; init; }
    public int OrdemExibicao { get; init; }
}

public record EmbalagemDetalhe
{
    public Guid EmbalagemId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Descricao { get; init; }
    public DimensoesDetalhe? Dimensoes { get; init; }
    public bool Padrao { get; init; }
}
