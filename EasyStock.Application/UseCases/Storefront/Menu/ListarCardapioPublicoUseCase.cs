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
                // Avulsos têm NomePublico/CategoriaTexto em minúsculo (factory). Capitaliza pra
                // exibição na vitrine (title-case pt-BR); nomes de Produto (vinculado, já
                // capitalizados) são preservados pelo guard de FormatarExibicao.
                Nome: FormatarExibicao(i.NomeEfetivo()) ?? string.Empty,
                Descricao: i.DescricaoPublica,
                PrecoCentavos: (long)Math.Round(i.PrecoEfetivo() * 100m, MidpointRounding.AwayFromZero),
                ImagemUrl: i.FotoUrl,
                // Avulso: null (frontend usa disponivel; estoqueAtual não se aplica).
                // Vinculado: 0 por ora — snapshot eventual fora deste escopo (TASK-EZ-MENU-001).
                EstoqueAtual: i.ProdutoId.HasValue ? 0 : null,
                Categoria: FormatarExibicao(i.CategoriaEfetiva()),
                Ordem: i.OrdemExibicao,
                Disponivel: i.Disponivel,
                Tag: i.Tag,
                PesoExibicao: i.PesoExibicao))
            .ToList();

        return new ListarCardapioPublicoResult(dtos, storefront.TituloPublico, storefront.Slug);
    }

    /// <summary>Preposições/conjunções que ficam minúsculas no meio do título (pt-BR).</summary>
    private static readonly HashSet<string> PalavrasMinusculas = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "com", "a", "o", "ao", "aos",
        "à", "às", "em", "no", "na", "nos", "nas", "para", "sem", "por", "ou",
    };

    /// <summary>
    /// Title-case pt-BR para exibição na vitrine. <c>NomePublico</c>/<c>CategoriaTexto</c> de
    /// itens avulsos são armazenados em minúsculo (factory). Capitaliza a 1ª letra de cada
    /// palavra (e após hífen), deixando preposições do meio minúsculas. <strong>Guard:</strong>
    /// só transforma se o valor vier TODO minúsculo — preserva nomes de Produto (itens
    /// vinculados) que já vêm capitalizados.
    /// </summary>
    private static string? FormatarExibicao(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        if (s != s.ToLowerInvariant()) return s; // já tem maiúscula → preserva (ex: nome de Produto)

        var palavras = s.Split(' ');
        for (var i = 0; i < palavras.Length; i++)
        {
            var p = palavras[i];
            if (p.Length == 0) continue;
            if (i > 0 && PalavrasMinusculas.Contains(p)) continue;

            var arr = p.ToCharArray();
            var capitalizar = true;
            for (var j = 0; j < arr.Length; j++)
            {
                if (arr[j] == '-') { capitalizar = true; continue; }
                if (capitalizar && char.IsLetter(arr[j]))
                {
                    arr[j] = char.ToUpperInvariant(arr[j]);
                    capitalizar = false;
                }
            }
            palavras[i] = new string(arr);
        }
        return string.Join(' ', palavras);
    }
}
