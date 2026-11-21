namespace EasyStock.Application.UseCases.Lotes;

/// <summary>DTOs do módulo Lote (Onda P5.A).</summary>
public sealed record LoteResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    string Codigo,
    string Status,
    DateTime DataProducao,
    Guid? OperadorUserId,
    string? OperadorNome,
    string? Observacoes,
    string? FotoUrl,
    string? Origem,
    string? MobileBatchId,
    int ItensCount,
    int TotalUnidades,
    int EtiquetasConferidas,
    int EtiquetasDivergentes,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    DateTime? FinalizadoEm,
    // #2199 (D4): número do código de barras; nulo só em lote anterior a ele que ainda não imprimiu.
    string? NumeroCurto = null
);

public sealed record LoteDetalheResult(
    LoteResult Lote,
    IReadOnlyList<LoteItemResult> Itens,
    IReadOnlyList<LoteEtiquetaResult> Etiquetas
);

public sealed record LoteItemResult(
    Guid Id, Guid LoteId, Guid? ProdutoId,
    string Nome, string? Emoji, string? Unidade,
    int Quantidade, int? PesoG, int? ValidadeDias, DateTime? ExpiraEm,
    string? FotoUrl, DateTime CriadoEm,
    // C2 (RDC 727/2022): tipo do produto vinculado (Avulso default | Embalado).
    // Usado pela UI para exibir badge "Sem peso" e habilitar backfill.
    string TipoEmbalagem = "Avulso",
    // #2373: medida exata da entrada (ex.: 0,3 kg); null = item de produção, vale Quantidade.
    decimal? QuantidadeMedida = null
);

public sealed record LoteEtiquetaResult(
    Guid Id, Guid LoteId, Guid LoteItemId,
    int Sequencial, string Codigo, string Status,
    DateTime? ConferidaEm, Guid? ConferidaPorUserId, string? ConferidaPorNome,
    string? ObservacaoConferencia, DateTime CriadoEm,
    string? LayoutSnapshotJson, string? LayoutSnapshotMeta
);
