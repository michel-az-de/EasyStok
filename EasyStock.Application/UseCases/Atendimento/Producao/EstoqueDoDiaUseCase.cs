using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Inventario.Desacertos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="DiasParaVencer">Negativo = já venceu. Null = lote sem validade.</param>
public sealed record LoteDoDia(string? Codigo, decimal Quantidade, DateTime? ValidadeEm, int? DiasParaVencer, bool Vencendo, bool Vencido);

/// <param name="Saldo">Porções em lotes com saldo e não vencidos (mesmo critério do cardápio, #1171).</param>
/// <param name="Descoberto">Porções vendidas sem saldo (S22), à espera de contagem.</param>
/// <param name="Porcoes">M1.4c (#1537): saldo de cada porção. Vazio em prato sem porções.</param>
public sealed record EstoqueDoPrato(
    Guid CardapioItemId, Guid ProdutoId, string Nome, string? Porcao, decimal Saldo, decimal Descoberto,
    IReadOnlyList<LoteDoDia> Lotes, IReadOnlyList<SaldoDaPorcao>? Porcoes = null);

/// <param name="VariacaoId">A porção do cardápio. Null = saldo sem porção (estoque antigo ou lote do PWA).</param>
public sealed record SaldoDaPorcao(Guid? VariacaoId, string Rotulo, decimal Saldo);

/// <param name="CardapioItemId">O prato do alerta, para o console ajustar pela rota do item. Null se não está no cardápio.</param>
public sealed record AlertaDeEstoque(Guid ProdutoId, Guid? CardapioItemId, string Nome, string Texto, decimal QuantidadeDescoberta);

public sealed record EstoqueDoDiaResult(IReadOnlyList<EstoqueDoPrato> Pratos, IReadOnlyList<AlertaDeEstoque> Alertas);

/// <summary>
/// Estoque do dia no console (M2.1, #1490): o que a Thati usa todo dia, em porções e por prato do
/// cardápio. Só lê o que o S22/S23 já gravam: lotes por produto, descoberto e os alertas em texto
/// de venda sem saldo. Prato avulso (sem produto) não tem saldo e fica fora; arquivado também.
/// Vencendo = vence hoje ou amanhã (US-065), no dia operacional do Brasil.
/// </summary>
public sealed class EstoqueDoDiaUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ListarDesacertosEstoqueUseCase desacertos,
    TimeProvider relogio)
{
    public const int DiasVencendo = 1;

    public async Task<EstoqueDoDiaResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();

        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var pratos = (await cardapioRepository.GetTodosDoStorefrontAsync(storefront.Id, ct))
            .Where(i => i.ProdutoId.HasValue && !i.EstaArquivado)
            .OrderBy(i => i.OrdemExibicao).ThenBy(i => i.CriadoEm).ThenBy(i => i.Id)
            .ToList();
        var lotesPorProduto = pratos.Count == 0
            ? new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>()
            : await itemEstoqueRepository.GetByProdutosAsync(empresaId, pratos.Select(p => p.ProdutoId!.Value).Distinct(), null, ct);

        var estoque = pratos.Select(p =>
        {
            var lotes = lotesPorProduto.TryGetValue(p.ProdutoId!.Value, out var l) ? l : [];
            var comSaldo = lotes
                .Where(i => i.QuantidadeAtual.Value > 0)
                .OrderBy(i => i.ValidadeEm?.DataValidade ?? DateTime.MaxValue).ThenBy(i => i.EntradaEm)
                .Select(i => Lote(i, hoje))
                .ToList();
            var (saldo, descoberto) = SaldoEDescoberto(lotes, hoje);
            return new EstoqueDoPrato(
                p.Id, p.ProdutoId!.Value, p.NomeEfetivo() ?? "(sem nome)", p.PesoExibicao, saldo, descoberto, comSaldo,
                SaldosPorPorcao(p, lotes, hoje));
        }).ToList();

        var pratoDoProduto = pratos.GroupBy(p => p.ProdutoId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
        var alertas = (await desacertos.ExecuteAsync(new ListarDesacertosEstoqueInput(empresaId), ct))
            .Select(d => new AlertaDeEstoque(d.ProdutoId, pratoDoProduto.TryGetValue(d.ProdutoId, out var id) ? id : null,
                d.Nome, d.Texto, d.QuantidadeDescoberta))
            .ToList();

        return new EstoqueDoDiaResult(estoque, alertas);
    }

    // M1.4c (#1537, D-M1-03): o saldo de cada porção vem dos lotes da variação dela. O que sobrar sem
    // porção (estoque antigo, lote do PWA) aparece à parte, porque a venda da porção cai nele quando
    // a porção não tem lote nenhum.
    private static List<SaldoDaPorcao> SaldosPorPorcao(CardapioItem prato, IReadOnlyCollection<ItemEstoque> lotes, DateOnly hoje)
    {
        if (!prato.TemVariacoes()) return [];
        var saldos = prato.Variacoes
            .OrderBy(v => v.OrdemExibicao).ThenBy(v => v.CriadoEm).ThenBy(v => v.Id)
            .Select(v => new SaldoDaPorcao(v.Id, v.Rotulo,
                v.ProdutoVariacaoId is { } id ? SaldoEDescoberto(lotes.Where(l => l.ProdutoVariacaoId == id), hoje).Saldo : 0m))
            .ToList();
        var semPorcao = SaldoEDescoberto(lotes.Where(l => l.ProdutoVariacaoId is null), hoje).Saldo;
        if (semPorcao > 0) saldos.Add(new SaldoDaPorcao(null, "sem porção", semPorcao));
        return saldos;
    }

    /// <summary>Saldo = porções em lotes não vencidos; descoberto = o que saiu sem saldo. Usado também pela M2.5.</summary>
    public static (decimal Saldo, decimal Descoberto) SaldoEDescoberto(IEnumerable<ItemEstoque> lotes, DateOnly hoje)
    {
        var lista = lotes as IReadOnlyCollection<ItemEstoque> ?? lotes.ToList();
        var saldo = lista
            .Where(i => i.QuantidadeAtual.Value > 0 && i.ValidadeEm?.DiasAteVencimento(hoje) is not < 0)
            .Sum(i => i.QuantidadeAtual.Value);
        return (saldo, lista.Sum(i => i.QuantidadeDescoberta.Value));
    }

    private static LoteDoDia Lote(ItemEstoque i, DateOnly hoje)
    {
        int? dias = i.ValidadeEm?.DiasAteVencimento(hoje);
        return new LoteDoDia(
            i.CodigoLote?.Value, i.QuantidadeAtual.Value, i.ValidadeEm?.DataValidade, dias,
            Vencendo: dias is >= 0 and <= DiasVencendo,
            Vencido: dias is < 0);
    }
}
