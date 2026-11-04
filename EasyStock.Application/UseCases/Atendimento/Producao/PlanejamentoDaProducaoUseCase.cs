using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="Agendados">Porções de pedidos agendados até a data que ainda vão consumir estoque.</param>
/// <param name="ProdutoId">Nulo no prato que ainda não foi produzido (#2082): sem produto não há saldo nem mínimo,
/// só os pedidos agendados.</param>
/// <param name="Sugestao">max(mínimo + agendados + descoberto − saldo, 0), em porções inteiras.</param>
public sealed record SugestaoDoPrato(
    Guid CardapioItemId, Guid? ProdutoId, string Nome, decimal Minimo, decimal Saldo, decimal Agendados, decimal Descoberto, decimal Sugestao);

public sealed record SugestaoDeProducaoResult(DateOnly Ate, IReadOnlyList<SugestaoDoPrato> Pratos);

/// <param name="ProdutoId">Nulo no prato sem produto (#2082): não tem receita e fica fora da conta dos insumos.</param>
/// <param name="PesoPorPorcaoG">M1.4e (#1552): peso de uma porção. Só entra na conta quando a receita rende em peso.</param>
public sealed record PratoPlanejado(Guid? ProdutoId, decimal Porcoes, int? PesoPorPorcaoG = null);

/// <summary>
/// Planejamento da produção no console (M2.5, #1502).
/// <para>
/// <b>Sugestão (D-M2-05 = a):</b> por prato do cardápio ligado ao estoque, quanto produzir para
/// atender os pedidos agendados até a data, cobrir o descoberto e voltar ao mínimo, descontado o
/// saldo. O pedido só baixa estoque ao ficar pronto, então o agendado ainda não está no saldo.
/// </para>
/// <para>
/// <b>Planejar:</b> insumos e faltas pela mesma cesta da calculadora mobile
/// (<see cref="CalcularCestaProducaoUseCase"/>): um use case, duas rotas. Não mexe no estoque.
/// </para>
/// </summary>
public sealed class PlanejamentoDaProducaoUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    IProdutoRepository produtoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IPedidoRepository pedidoRepository,
    CalcularCestaProducaoUseCase cesta,
    TimeProvider relogio)
{
    public async Task<SugestaoDeProducaoResult> SugestaoAsync(Guid empresaId, DateOnly? ate = null, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo) throw new StorefrontNaoEncontradoException();

        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var limite = ate ?? hoje.AddDays(1);
        if (limite < hoje) throw new UseCaseValidationException("A data do planejamento já passou.");

        var pratos = (await cardapioRepository.GetTodosDoStorefrontAsync(storefront.Id, ct))
            .Where(i => !i.EstaArquivado)
            .OrderBy(i => i.OrdemExibicao).ThenBy(i => i.CriadoEm).ThenBy(i => i.Id)
            .ToList();
        if (pratos.Count == 0) return new SugestaoDeProducaoResult(limite, []);

        var produtoIds = pratos.Where(p => p.ProdutoId.HasValue).Select(p => p.ProdutoId!.Value).Distinct().ToList();
        var demanda = await pedidoRepository.GetDemandaAgendadaAsync(empresaId, HorarioBrasil.JanelaDiaUtc(limite).FimUtc, ct);
        // #1575: aqui os lotes só entram em SaldoEDescoberto, que ignora lote zerado e sem descoberto.
        IReadOnlyDictionary<Guid, IReadOnlyCollection<ItemEstoque>> lotes = produtoIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>()
            : await itemEstoqueRepository.GetByProdutosAsync(empresaId, produtoIds, null, ct, apenasComSaldoOuDescoberto: true);
        Dictionary<Guid, Produto> produtos = produtoIds.Count == 0
            ? []
            : (await produtoRepository.GetByIdsAsync(empresaId, produtoIds, ct)).ToDictionary(p => p.Id);

        var lista = new List<SugestaoDoPrato>(pratos.Count);
        foreach (var prato in pratos)
        {
            if (prato.ProdutoId is null)
            {
                // #2082: prato nunca produzido (sem produto, saldo 0). Só entra se há pedido agendado, casado pelo
                // prato do cardápio; sem pedido não há o que sugerir e a lista não ganha todo prato do cardápio.
                var pedidos = demanda.Where(d => d.CardapioItemId == prato.Id).Sum(d => d.Quantidade);
                if (pedidos > 0)
                    lista.Add(new SugestaoDoPrato(prato.Id, null, prato.NomeEfetivo() ?? "Prato",
                        0m, 0m, pedidos, 0m, Math.Ceiling(pedidos)));
                continue;
            }

            var produtoId = prato.ProdutoId.Value;
            if (!produtos.TryGetValue(produtoId, out var produto)) continue;

            var (saldo, descoberto) = EstoqueDoDiaUseCase.SaldoEDescoberto(lotes.TryGetValue(produtoId, out var l) ? l : [], hoje);
            // O item do pedido aponta para o prato do cardápio; o antigo (sem prato) cai pelo produto.
            var agendados = demanda
                .Where(d => d.CardapioItemId == prato.Id || (d.CardapioItemId is null && d.ProdutoId == produtoId))
                .Sum(d => d.Quantidade);
            decimal minimo = produto.QuantidadeMinima ?? 0;
            var sugestao = Math.Max(Math.Ceiling(minimo + agendados + descoberto - saldo), 0m);

            lista.Add(new SugestaoDoPrato(prato.Id, produtoId, prato.NomeEfetivo() ?? produto.Nome,
                minimo, saldo, agendados, descoberto, sugestao));
        }
        return new SugestaoDeProducaoResult(limite, lista);
    }

    public async Task<CalcularCestaProducaoResult> PlanejarAsync(Guid empresaId, IReadOnlyList<PratoPlanejado>? pratos, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var itens = new List<ItemCestaInput>();
        var comPorcoes = (pratos ?? []).Where(p => p.Porcoes > 0).ToList();
        // #2082: o prato que nunca foi produzido não tem produto, logo não tem receita: fora da conta, sem derrubar o resto.
        foreach (var doPrato in comPorcoes.Where(p => p.ProdutoId.HasValue).GroupBy(p => p.ProdutoId!.Value))
            itens.Add(await ItemDaCestaAsync(empresaId, doPrato.Key, doPrato.ToList()));
        if (itens.Count == 0)
            throw new UseCaseValidationException(comPorcoes.Count > 0
                ? "Esses pratos ainda não foram produzidos e não têm receita: não há insumos para calcular."
                : "Diga quantas porções de ao menos um prato.");
        return await cesta.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, itens), ct);
    }

    // M1.4e (#1552): a receita que rende em peso é planejada pelo peso (porções × peso de cada uma). Sem o
    // peso, o prato vai em porções e a cesta o deixa de fora por unidade incompatível, como sempre foi.
    private async Task<ItemCestaInput> ItemDaCestaAsync(Guid empresaId, Guid produtoId, IReadOnlyList<PratoPlanejado> linhas)
    {
        var emPorcoes = new ItemCestaInput(produtoId, linhas.Sum(p => p.Porcoes), UnidadeMedida.Un);
        if (linhas.Any(p => p.PesoPorPorcaoG is not > 0)) return emPorcoes;

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId);
        return produto is null || produto.RendimentoUnidade == UnidadeMedida.Un
            ? emPorcoes
            : new ItemCestaInput(produtoId, linhas.Sum(p => p.Porcoes * p.PesoPorPorcaoG!.Value), UnidadeMedida.G);
    }
}
