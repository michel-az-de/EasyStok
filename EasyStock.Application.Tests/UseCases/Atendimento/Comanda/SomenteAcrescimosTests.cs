using EasyStock.Application.UseCases.Atendimento.Comanda;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// #2018 (comanda-13): "só acréscimos passam direto" estava no <c>ComandaClienteController</c> (inline, sobre o DTO do estado) e
/// no <see cref="ConfirmarAlteracaoComandaUseCase"/> (sobre as entidades). A regra é uma só, a do use case. Estes casos
/// repetem o predicado antigo do controller e provam que ele e a regra única concordam no que passa direto.
/// </summary>
public class SomenteAcrescimosTests
{
    private static readonly Guid Lasanha = Guid.NewGuid();
    private static readonly Guid Torta = Guid.NewGuid();

    private static PedidoItem Antigo(Guid prato, decimal qtd, string? obs = null, Guid? porcao = null) => new()
    {
        Id = Guid.NewGuid(), Nome = "prato", CardapioItemId = prato, CardapioItemVariacaoId = porcao, Quantidade = qtd,
        PrecoUnitario = 10m, Subtotal = qtd * 10m, Observacao = obs,
    };

    private static ItemSelecionadoComanda Novo(Guid prato, int qtd, string? obs = null, Guid? porcao = null) =>
        new(prato, porcao, "prato", qtd, 10m, obs);

    /// <summary>O predicado que o controller carregava (estado.Selecao x pedido.Itens), copiado para caracterizar.</summary>
    private static bool PredicadoAntigoDoController(IReadOnlyList<PedidoItem> antigos, IReadOnlyList<ItemSelecionadoComanda> selecao) =>
        antigos.GroupBy(a => (a.CardapioItemId, VariacaoId: a.CardapioItemVariacaoId)).All(g => selecao.Any(n =>
            n.CardapioItemId == g.Key.CardapioItemId && n.VariacaoId == g.Key.VariacaoId && n.Qtd >= g.Sum(a => a.Quantidade)
            && g.All(a => (n.Observacao ?? "") == (a.Observacao ?? ""))));

    public static TheoryData<string, PedidoItem[], ItemSelecionadoComanda[], bool> Casos => new()
    {
        { "mais uma unidade", [Antigo(Lasanha, 1)], [Novo(Lasanha, 2)], true },
        { "mesma quantidade", [Antigo(Lasanha, 1)], [Novo(Lasanha, 1)], true },
        { "prato novo junto", [Antigo(Lasanha, 1)], [Novo(Lasanha, 1), Novo(Torta, 1)], true },
        { "tirou unidade", [Antigo(Lasanha, 2)], [Novo(Lasanha, 1)], false },
        { "tirou o prato", [Antigo(Lasanha, 1), Antigo(Torta, 1)], [Novo(Lasanha, 1)], false },
        { "trocou o prato", [Antigo(Lasanha, 1)], [Novo(Torta, 1)], false },
        { "mudou a observacao", [Antigo(Lasanha, 1, "sem cebola")], [Novo(Lasanha, 2, "com cebola")], false },
        { "manteve a observacao", [Antigo(Lasanha, 1, "sem cebola")], [Novo(Lasanha, 2, "sem cebola")], true },
        { "observacao vazia e nula sao iguais", [Antigo(Lasanha, 1, "")], [Novo(Lasanha, 2, null)], true },
        { "linhas do mesmo prato somam", [Antigo(Lasanha, 1), Antigo(Lasanha, 1)], [Novo(Lasanha, 2)], true },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void RegraUnica_ConcordaComOPredicadoAntigoDoController(string caso, PedidoItem[] antigos, ItemSelecionadoComanda[] selecao, bool esperado)
    {
        ConfirmarAlteracaoComandaUseCase.SomenteAcrescimos(antigos, selecao).Should().Be(esperado, caso);
        PredicadoAntigoDoController(antigos, selecao).Should().Be(esperado, "o predicado antigo do controller dava o mesmo: " + caso);
    }
}
