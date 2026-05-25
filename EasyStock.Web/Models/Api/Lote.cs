namespace EasyStock.Web.Models.Api;

public record Lote
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public required string Codigo { get; init; }
    public required string Status { get; init; }
    public DateTime DataProducao { get; init; }
    public Guid? OperadorUserId { get; init; }
    public string? OperadorNome { get; init; }
    public string? Observacoes { get; init; }
    public string? FotoUrl { get; init; }
    public string? Origem { get; init; }
    public string? MobileBatchId { get; init; }
    public int ItensCount { get; init; }
    public int TotalUnidades { get; init; }
    public int EtiquetasConferidas { get; init; }
    public int EtiquetasDivergentes { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime AlteradoEm { get; init; }
    public DateTime? FinalizadoEm { get; init; }
}

public record LoteDetalhe
{
    public required Lote Lote { get; init; }
    public List<LoteItemDto> Itens { get; init; } = new();
    public List<LoteEtiquetaDto> Etiquetas { get; init; } = new();
}

public record LoteItemDto(
    string Id, string LoteId, Guid? ProdutoId,
    string Nome, string? Emoji, string? Unidade,
    int Quantidade, int? PesoG, int? ValidadeDias, DateTime? ExpiraEm,
    string? FotoUrl, DateTime CriadoEm,
    // C2 (RDC 727/2022): "Avulso" (default) | "Embalado". Habilita badge "Sem peso" e backfill.
    string TipoEmbalagem = "Avulso");

public record LoteEtiquetaDto(
    string Id, string LoteId, string LoteItemId,
    int Sequencial, string Codigo, string Status,
    DateTime? ConferidaEm, Guid? ConferidaPorUserId, string? ConferidaPorNome,
    string? ObservacaoConferencia, DateTime CriadoEm);

public record MobileBatchSummary
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public string? Lote { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public Guid? ErpLoteId { get; init; }
    public string? LastDeviceId { get; init; }
    public string? LastOperatorName { get; init; }
    public bool Linked => ErpLoteId.HasValue && ErpLoteId.Value != Guid.Empty;
}
