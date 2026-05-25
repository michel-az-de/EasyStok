namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record EtiquetaTemplateSistemaResult(
    Guid Id, string Codigo, string Nome, string? Descricao, string LayoutJson, int Ordem);

public sealed record EtiquetaTemplateResult(
    Guid Id, Guid EmpresaId, string Nome, Guid? BaseSistemaId,
    string LayoutJson, bool IsDefault,
    DateTime CriadoEm, DateTime AlteradoEm);

public sealed record EtiquetaTemplateListItem(
    string Origem,  // "Sistema" | "Empresa"
    Guid Id, string Nome, string? Descricao,
    string LayoutJson, bool IsDefault, int? Ordem,
    Guid? BaseSistemaId,
    DateTime? CriadoEm, DateTime? AlteradoEm);

public sealed record EtiquetaEmpresaDefaultResult(
    Guid EmpresaId, string TemplateOrigem, Guid TemplateId, DateTime AlteradoEm);

public sealed record EmpresaRenderDto(string Nome, string? LogoUrl);

public sealed record ProdutoRenderDto(
    Guid Id, string Nome, string? Marca,
    string? Emoji, string? Unidade,
    // C2 (RDC 727/2022): peso unitário em gramas vindo do LoteItem.
    // Null para Avulso (esperado), preenchido para Embalado (obrigatório).
    int? PesoG,
    // Ficha técnica (null se não preenchida)
    decimal? FichaKcal, decimal? FichaProteinaG, decimal? FichaCarbsG,
    decimal? FichaGorduraG, decimal? FichaGorduraSaturadaG,
    decimal? FichaFibrasG, decimal? FichaSodioMg, decimal? FichaPorcaoG,
    string? FichaModoPreparo, IReadOnlyList<string> FichaAlergenos,
    bool TemFicha);

public sealed record EtiquetaRenderItem(
    Guid Id, int Sequencial, string Codigo, string Status,
    ProdutoRenderDto Produto,
    string? LoteCodigo, DateTime? LoteValidadeEm, DateTime LoteCriadoEm,
    string? LayoutSnapshotJson, string? LayoutSnapshotMeta);

public sealed record EtiquetaRenderPayload(
    string LayoutJson,
    EmpresaRenderDto Empresa,
    IReadOnlyList<EtiquetaRenderItem> Etiquetas,
    IReadOnlyList<Guid> ProdutosSemFicha);

public sealed record MarcarImpressasRequest(
    IReadOnlyList<Guid> Ids,
    string LayoutJson,
    LayoutSnapshotMetaDto LayoutMeta,
    string Status);

public sealed record LayoutSnapshotMetaDto(
    string Origem, Guid Id, string Nome);
