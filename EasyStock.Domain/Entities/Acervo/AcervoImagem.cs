namespace EasyStock.Domain.Entities.Acervo;

public sealed class AcervoImagem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid CriadoPorId { get; set; }
    public string Titulo { get; set; } = "";
    public string Origem { get; set; } = "";
    public string Formato { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Tamanho { get; set; }
    public string Prompt { get; set; } = "";
    public string Referencias { get; set; } = "";
    public string ItensJson { get; set; } = "[]";
    public Guid? OriginalId { get; set; }
    public Guid? SolicitacaoId { get; set; }
    public string? CanvaUrl { get; set; }
    public string? CanvaAssetId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AprovadoEm { get; set; }
    public Guid? AprovadoPorId { get; set; }
}

public sealed class AcervoPublicacao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ImagemId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? ProdutoId { get; set; }
    public Guid UsuarioId { get; set; }
    public bool Galeria { get; set; }
    public string Url { get; set; } = "";
    public string? CapaAnterior { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public sealed class AcervoSolicitacao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid ItemId { get; set; }
    public string Formato { get; set; } = "";
    public string Brief { get; set; } = "";
    public string ReferenciasJson { get; set; } = "[]";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ConcluidoEm { get; set; }
}
