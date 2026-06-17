using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Resolve o storefront público por slug e retorna a lista de
/// <c>CardapioItem</c> visíveis (Visivel=true) como <see cref="CardapioItemPublicoDto"/>.
///
/// <para>
/// <strong>Anônimo</strong> — endpoint não exige autenticação. Multi-tenancy via
/// slug (chave de entrada). Sem risco de vazamento cross-tenant porque nenhuma
/// requisição pública carrega <c>EmpresaId</c> no contexto.
/// </para>
///
/// <para>
/// <strong>Storefront inativo</strong> retorna <see cref="StorefrontNaoEncontradoException"/>
/// (não 403) — não vaza existência do tenant para o público.
/// </para>
///
/// <para>
/// <strong>Ordenação</strong>: Categoria.Nome ASC → OrdemExibicao ASC. Items sem
/// categoria caem por último (Categoria=null ordena como string vazia depois de
/// nomes preenchidos via convenção StringComparer.Ordinal — empurrados para o
/// fim usando sentinela <c>"￿"</c>).
/// </para>
///
/// <para>
/// <strong>Preço</strong> retornado em centavos (long) — evita float no transit.
/// <c>PrecoStorefront</c> override OU <c>Produto.PrecoReferencia</c> como fallback.
/// </para>
/// </summary>
public sealed class ListarCardapioPublicoUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    ILogger<ListarCardapioPublicoUseCase> logger)
{
    /// <summary>Sentinela para empurrar items sem categoria para o fim da ordenação.</summary>
    private const string SemCategoriaSentinela = "￿";

    public async Task<ListarCardapioPublicoResult> ExecuteAsync(
        ListarCardapioPublicoInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var slug = (input.Slug ?? string.Empty).Trim().ToLowerInvariant();
        var storefront = await storefrontRepository.GetBySlugAsync(slug, ct);
        if (storefront is null || !storefront.Ativo)
        {
            logger.LogInformation(
                "Cardápio público solicitado para storefront inexistente/inativo: slug={Slug}",
                slug);
            throw new StorefrontNaoEncontradoException(slug);
        }

        var itens = await cardapioItemRepository.GetVisiveisDoStorefrontAsync(storefront.Id, ct);

        var dtos = itens
            // CategoriaTexto ?? Produto.Categoria.Nome: avulsos usam CategoriaTexto;
            // vinculados usam Produto.Categoria.Nome como fallback.
            // Sentinela empurra itens sem categoria para o fim.
            .OrderBy(i => i.CategoriaEfetiva() ?? SemCategoriaSentinela, StringComparer.Ordinal)
            .ThenBy(i => i.OrdemExibicao)
            // Desempate determinístico (CriadoEm → Id): itens nascem OrdemExibicao=0 (factory),
            // então um menu nunca-reordenado é todo-empate; sem desempate a ordem do array varia
            // entre queries/instâncias → o ETag (hash do payload) fica instável → 304-thrash em vez
            // de cache-hit. CriadoEm preserva ordem-de-inserção; Id fecha o determinismo total.
            .ThenBy(i => i.CriadoEm)
            .ThenBy(i => i.Id)
            .Select(i => new CardapioItemPublicoDto(
                Id: i.Id,
                Nome: i.NomeEfetivo() ?? string.Empty,
                Descricao: i.DescricaoPublica,
                PrecoCentavos: (long)Math.Round(i.PrecoEfetivo() * 100m, MidpointRounding.AwayFromZero),
                ImagemUrl: i.FotoUrl,
                // Avulso: null (frontend usa disponivel; estoqueAtual não se aplica).
                // Vinculado: 0 por ora — snapshot eventual fora deste escopo (TASK-EZ-MENU-001).
                EstoqueAtual: i.ProdutoId.HasValue ? 0 : null,
                Categoria: i.CategoriaEfetiva(),
                Ordem: i.OrdemExibicao,
                Disponivel: i.Disponivel,
                Tag: i.Tag))
            .ToList();

        return new ListarCardapioPublicoResult(dtos, storefront.TituloPublico, storefront.Slug);
    }
}
