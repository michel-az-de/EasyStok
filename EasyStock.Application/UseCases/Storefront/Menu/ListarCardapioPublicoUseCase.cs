using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
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
    IItemEstoqueRepository itemEstoqueRepository,
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
        var saldos = await ObterSaldosAsync(storefront.EmpresaId, itens, ct);

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
                // Vinculado (#1171): saldo produzido, somado dos lotes com saldo e não vencidos.
                EstoqueAtual: i.ProdutoId.HasValue ? SaldoInteiro(saldos, i.ProdutoId.Value) : null,
                Categoria: FormatarExibicao(i.CategoriaEfetiva()),
                Ordem: i.OrdemExibicao,
                // #1171: segue flag manual. Saldo 0 NÃO esgota o item: o checkout recusa
                // !Disponivel (#1158) e falta de estoque avisa, não trava o pedido (S17).
                Disponivel: i.Disponivel,
                Tag: i.Tag,
                PesoExibicao: i.PesoExibicao,
                Linha: i.Linha.ParaContrato(),
                TempoPreparoMinutos: i.TempoPreparoMinutos))
            .ToList();

        return new ListarCardapioPublicoResult(dtos, storefront.TituloPublico, storefront.Slug);
    }

    /// <summary>
    /// Uma consulta de saldos para todos os itens vinculados (sem N+1). Sem item vinculado,
    /// não vai ao banco. <paramref name="empresaId"/> vem do storefront resolvido pelo slug.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> ObterSaldosAsync(
        Guid empresaId, IReadOnlyList<CardapioItem> itens, CancellationToken ct)
    {
        var produtoIds = itens
            .Where(i => i.ProdutoId.HasValue)
            .Select(i => i.ProdutoId!.Value)
            .Distinct()
            .ToList();
        if (produtoIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        return await itemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(empresaId, produtoIds, ct);
    }

    /// <summary>Contrato expõe inteiro: arredonda para baixo (não promete fração que não existe inteira).</summary>
    private static int SaldoInteiro(IReadOnlyDictionary<Guid, decimal> saldos, Guid produtoId) =>
        saldos.TryGetValue(produtoId, out var saldo) && saldo > 0
            ? (int)Math.Floor(Math.Min(saldo, int.MaxValue))
            : 0;

    /// <summary>Preposições/conjunções que ficam minúsculas no meio do título (pt-BR).</summary>
    private static readonly HashSet<string> PalavrasMinusculas = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "com", "a", "o", "ao", "aos",
        "à", "às", "em", "no", "na", "nos", "nas", "para", "sem", "por", "ou",
    };

    /// <summary>Unidades que seguem um número e ficam minúsculas ("500 g", "250 ml"), #1334.</summary>
    private static readonly HashSet<string> UnidadesDeMedida = new(StringComparer.Ordinal)
    {
        "g", "kg", "ml", "l",
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
            if (i > 0 && UnidadesDeMedida.Contains(p) && palavras[i - 1].Length > 0 && char.IsDigit(palavras[i - 1][^1])) continue;

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
