namespace EasyStock.Web.Models.Api;

/// <summary>Resumo da vitrine do tenant — espelha VitrineResumoResponse (/api/minha-vitrine).</summary>
public record VitrineResumoApi(Guid StorefrontId, string Slug, string TituloPublico, bool Ativo);

/// <summary>
/// Item do cardápio na visão de gestão — espelha CardapioItemAdminListItem
/// (ListarCardapioAdminResult). <see cref="Avulso"/> é derivado para a View.
/// </summary>
public record CardapioItemApi(
    Guid Id,
    Guid? ProdutoId,
    string NomeEfetivo,
    double OrdemExibicao,
    decimal PrecoEfetivo,
    decimal? PrecoStorefrontOverride,
    bool Visivel,
    bool Disponivel,
    string? Tag,
    string? FotoUrl,
    string? PesoExibicao,
    string? CategoriaTexto)
{
    /// <summary>Item sem ligação com o estoque do sistema (ProdutoId nulo).</summary>
    public bool Avulso => ProdutoId is null;
}

/// <summary>Lista do cardápio + dados do storefront — espelha ListarCardapioAdminResult.</summary>
public record CardapioListaApi(
    Guid StorefrontId,
    string StorefrontSlug,
    string StorefrontTitulo,
    List<CardapioItemApi> Itens);

/// <summary>
/// Detalhe COMPLETO de um item — espelha CardapioItemDetalheAdmin (GET-by-id em
/// /api/minha-vitrine/cardapio/{itemId}). Carrega os "detalhes para o cliente" que a
/// listagem enxuta não traz; alimenta o prefill do formulário de edição.
/// </summary>
public record CardapioItemDetalheApi(
    Guid Id,
    Guid? ProdutoId,
    string? NomePublico,
    string NomeEfetivo,
    double OrdemExibicao,
    decimal PrecoEfetivo,
    decimal? PrecoStorefront,
    bool Visivel,
    bool Disponivel,
    string? Tag,
    string? FotoUrl,
    string? PesoExibicao,
    string? CategoriaTexto,
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string FiltrosJson)
{
    /// <summary>Item sem ligação com o estoque do sistema (ProdutoId nulo).</summary>
    public bool Avulso => ProdutoId is null;
}

/// <summary>
/// Payload de criação/edição vindo do formulário. Convenção espelhada do backend:
/// <c>null</c> = "não tocar" (no editar) / não enviar; <c>""</c> = limpar o campo.
/// O controller decide o que mandar (NomePublico vazio vira null no vinculado; demais
/// opcionais vazios viram "" para limpar).
/// </summary>
public record CardapioItemFormApi(
    Guid? ProdutoId,
    string? NomePublico,
    decimal? PrecoStorefront,
    string? CategoriaTexto,
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string? PesoExibicao,
    string? Tag,
    bool Visivel);

/// <summary>Id do item recém-criado — espelha AdicionarCardapioItemAdminResult.</summary>
public record CardapioCriarResultApi(Guid ItemId);

/// <summary>URL pública da foto recém-enviada — espelha UploadedFileResult.</summary>
public record CardapioFotoResultApi(string Url);

/// <summary>Estado real do publicar após o toggle (reconcilia a UI otimista).</summary>
public record ToggleVisivelResultApi(Guid ItemId, bool VisivelAgora);

/// <summary>Estado real da disponibilidade após o toggle (reconcilia a UI otimista).</summary>
public record ToggleDisponivelResultApi(Guid ItemId, bool DisponivelAgora);
