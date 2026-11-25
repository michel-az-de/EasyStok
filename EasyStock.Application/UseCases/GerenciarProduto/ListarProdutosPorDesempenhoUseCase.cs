namespace EasyStock.Application.UseCases.GerenciarProduto;

/// <summary>Ordem, sinal e filtros da lista de Produtos quando ela depende das vendas (#3303).</summary>
public sealed record FiltroPorDesempenho(string? Sort, string? Order, SinalDoProduto? Sinal, StatusProduto? Status, Guid? CategoriaId);

/// <summary>
/// #3303: a lista de Produtos ordenada pelas vendas ou filtrada por um sinal da faixa. Usa as linhas de
/// <see cref="DesempenhoDosProdutosUseCase"/>, para a lista e a faixa contarem igual, e pagina em memória: o catálogo de
/// uma loja cabe em memória e o desempenho já lê todos os produtos. A lista sem vendas continua no repositório, com cache.
/// </summary>
public sealed class ListarProdutosPorDesempenhoUseCase(DesempenhoDosProdutosUseCase desempenho, IProdutoRepository produtos)
{
    public const string Receita90 = "receita90";
    public const string SemVender = "semvender";
    private static readonly string[] Ordens = [Receita90, SemVender, "nome", "criadoem", "alteradoem"];

    public static bool OrdemPorVendas(string? sort) => sort?.ToLowerInvariant() is Receita90 or SemVender;

    public async Task<(IReadOnlyList<Produto> Produtos, int Total)> ExecuteAsync(
        Guid empresaId, FiltroPorDesempenho filtro, int page, int pageSize, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var sort = (filtro.Sort ?? Receita90).ToLowerInvariant();
        if (!Ordens.Contains(sort)) throw new ArgumentException($"Ordem desconhecida: {filtro.Sort}.", nameof(filtro));
        var desc = !string.Equals(filtro.Order, "asc", StringComparison.OrdinalIgnoreCase);

        var linhas = (await desempenho.ExecuteAsync(empresaId, ct)).Produtos
            .Where(l => filtro.Sinal is not { } s || DesempenhoDosProdutosUseCase.TemSinal(l, s))
            .ToDictionary(l => l.ProdutoId);
        var entidades = (await produtos.GetByIdsAsync(empresaId, linhas.Keys, ct))
            .Where(p => p.EmpresaId == empresaId)
            .Where(p => filtro.Status is not { } st || p.Status == st)
            .Where(p => filtro.CategoriaId is not { } c || p.CategoriaId == c)
            .ToList();

        var ordenados = Ordenar(entidades, linhas, sort, desc).ToList();
        var pagina = ordenados.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (pagina, ordenados.Count);
    }

    private static IOrderedEnumerable<Produto> Ordenar(List<Produto> lista, Dictionary<Guid, DesempenhoDeUmProduto> linhas, string sort, bool desc)
    {
        var porNome = StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), ignoreCase: true);
        IOrderedEnumerable<Produto> ordem = sort switch
        {
            // Quem vende mais primeiro; quem não vendeu fica junto no fim, por nome.
            Receita90 => lista.OrderByDescending(p => linhas[p.Id].Receita).ThenByDescending(p => linhas[p.Id].Unidades).ThenBy(p => p.Nome, porNome),
            // Produto de venda ativo que já vendeu, do mais esquecido ao mais recente; depois os que nunca venderam; o resto no fim.
            SemVender => lista.OrderBy(p => GrupoSemVender(linhas[p.Id])).ThenBy(p => linhas[p.Id].UltimaVenda).ThenBy(p => p.Nome, porNome),
            "nome" => desc ? lista.OrderByDescending(p => p.Nome, porNome) : lista.OrderBy(p => p.Nome, porNome),
            "criadoem" => desc ? lista.OrderByDescending(p => p.CriadoEm) : lista.OrderBy(p => p.CriadoEm),
            _ => desc ? lista.OrderByDescending(p => p.AlteradoEm) : lista.OrderBy(p => p.AlteradoEm),
        };
        return ordem.ThenBy(p => p.Id);
    }

    private static int GrupoSemVender(DesempenhoDeUmProduto l) => !l.AtivoDeVenda ? 2 : l.UltimaVenda is null ? 1 : 0;
}
