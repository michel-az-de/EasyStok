using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="Agendados">Porções de pedidos agendados até a data que ainda vão consumir estoque.</param>
/// <param name="Sugestao">max(mínimo + agendados + descoberto − saldo, 0), em porções inteiras.</param>
public sealed record SugestaoDoPrato(
    Guid CardapioItemId, Guid ProdutoId, string Nome, decimal Minimo, decimal Saldo, decimal Agendados, decimal Descoberto, decimal Sugestao);

public sealed record SugestaoDeProducaoResult(DateOnly Ate, IReadOnlyList<SugestaoDoPrato> Pratos);

public sealed record PratoPlanejado(Guid ProdutoId, decimal Porcoes);

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
            .Where(i => i.ProdutoId.HasValue && !i.EstaArquivado)
            .OrderBy(i => i.OrdemExibicao).ThenBy(i => i.CriadoEm).ThenBy(i => i.Id)
            .ToList();
        if (pratos.Count == 0) return new SugestaoDeProducaoResult(limite, []);

        var lotes = await itemEstoqueRepository.GetByProdutosAsync(empresaId, pratos.Select(p => p.ProdutoId!.Value).Distinct(), null, ct);
        var demanda = await pedidoRepository.GetDemandaAgendadaAsync(empresaId, HorarioBrasil.JanelaDiaUtc(limite).FimUtc, ct);

        var lista = new List<SugestaoDoPrato>(pratos.Count);
        foreach (var prato in pratos)
        {
            var produtoId = prato.ProdutoId!.Value;
            var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId);
            if (produto is null) continue;

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

    public Task<CalcularCestaProducaoResult> PlanejarAsync(Guid empresaId, IReadOnlyList<PratoPlanejado>? pratos, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var itens = (pratos ?? [])
            .Where(p => p.Porcoes > 0)
            .GroupBy(p => p.ProdutoId)
            .Select(g => new ItemCestaInput(g.Key, g.Sum(p => p.Porcoes), UnidadeMedida.Un))
            .ToList();
        if (itens.Count == 0) throw new UseCaseValidationException("Diga quantas porções de ao menos um prato.");
        return cesta.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, itens), ct);
    }
}
