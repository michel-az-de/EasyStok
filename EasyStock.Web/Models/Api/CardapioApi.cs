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
