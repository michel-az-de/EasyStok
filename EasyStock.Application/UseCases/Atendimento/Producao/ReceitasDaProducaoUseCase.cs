using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="CustoPorRendimento">Custo da receita ÷ rendimento (por porção quando o rendimento é em Un). Null = algum insumo sem custo.</param>
/// <param name="BaixaAutomatica">D-M2-01 (#1499): produzir o prato baixa os insumos da receita.</param>
public sealed record ReceitaDoPrato(
    Guid CardapioItemId, Guid ProdutoId, string Nome, decimal RendimentoBase, UnidadeMedida RendimentoUnidade,
    int Linhas, decimal? CustoTotal, decimal? CustoPorRendimento, bool BaixaAutomatica = false);

/// <param name="Custo">Custo da linha na unidade do insumo. Null = insumo sem custo ou unidades incompatíveis.</param>
public sealed record LinhaDaReceita(
    Guid InsumoId, string Insumo, decimal Quantidade, UnidadeMedida Unidade, UnidadeMedida UnidadeDoInsumo, decimal? Custo);

public sealed record DetalheDaReceita(
    Guid ProdutoId, string Nome, decimal RendimentoBase, UnidadeMedida RendimentoUnidade, UnidadeMedida UnidadeMedidaBase,
    IReadOnlyList<LinhaDaReceita> Linhas, decimal? CustoTotal, decimal? CustoPorRendimento, bool BaixaAutomatica = false);

/// <summary>
/// Receitas pelo console (M2.4a, #1498). Na API a "ficha técnica" é a nutricional; aqui é a
/// **receita** (BOM de 1 nível, <c>ProdutoComposicao</c>). Lê e calcula o custo por porção com a
/// unidade convertida (#1498: custo do insumo é por unidade-base dele). Gravar é o
/// <c>PUT api/produtos/{id}/composicao</c> que já existe (Gerente desde a #1462), com auditoria.
/// </summary>
public sealed class ReceitasDaProducaoUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    IProdutoRepository produtoRepository,
    IProdutoComposicaoRepository composicaoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<ReceitaDoPrato>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo) throw new StorefrontNaoEncontradoException();

        var pratos = (await cardapioRepository.GetTodosDoStorefrontAsync(storefront.Id, ct))
            .Where(i => i.ProdutoId.HasValue && !i.EstaArquivado)
            .OrderBy(i => i.OrdemExibicao).ThenBy(i => i.CriadoEm).ThenBy(i => i.Id);

        var lista = new List<ReceitaDoPrato>();
        foreach (var prato in pratos)
        {
            var produto = await produtoRepository.GetByIdAsync(empresaId, prato.ProdutoId!.Value);
            if (produto is null) continue;
            var linhas = await composicaoRepository.GetByProdutoFinalAsync(empresaId, produto.Id, null, ct);
            var (total, porRendimento) = Custos(linhas.Select(Linha).ToList(), produto.RendimentoBase);
            lista.Add(new ReceitaDoPrato(prato.Id, produto.Id, prato.NomeEfetivo() ?? produto.Nome,
                produto.RendimentoBase, produto.RendimentoUnidade, linhas.Count, total, porRendimento, produto.BaixaInsumoAutomatica));
        }
        return lista;
    }

    public async Task<DetalheDaReceita> ObterAsync(Guid empresaId, Guid produtoId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Prato não encontrado.");
        var linhas = (await composicaoRepository.GetByProdutoFinalAsync(empresaId, produtoId, null, ct))
            .OrderBy(c => c.OrdemExibicao).Select(Linha).ToList();
        var (total, porRendimento) = Custos(linhas, produto.RendimentoBase);
        return new DetalheDaReceita(produto.Id, produto.Nome, produto.RendimentoBase, produto.RendimentoUnidade,
            produto.UnidadeMedidaBase, linhas, total, porRendimento, produto.BaixaInsumoAutomatica);
    }

    /// <summary>D-M2-01 (#1499): liga ou desliga a baixa de insumo na produção. Ligar exige receita.</summary>
    public async Task MarcarBaixaAutomaticaAsync(Guid empresaId, Guid produtoId, bool ligada, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Prato não encontrado.");
        if (ligada && (await composicaoRepository.GetByProdutoFinalAsync(empresaId, produtoId, null, ct)).Count == 0)
            throw new UseCaseValidationException("Monte a receita antes de ligar a baixa automática.");

        produto.BaixaInsumoAutomatica = ligada;
        produto.AlteradoEm = DateTime.UtcNow;
        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();
    }

    private static LinhaDaReceita Linha(ProdutoComposicao c)
    {
        var insumo = c.Insumo!;
        decimal? custo = insumo.CustoReferencia is { } custoRef
            ? CalculoProducaoCore.CustoNaUnidadeDoInsumo(c.Quantidade, c.Unidade, insumo.UnidadeMedidaBase, custoRef.Valor)
            : null;
        return new LinhaDaReceita(c.InsumoId, insumo.Nome, c.Quantidade, c.Unidade, insumo.UnidadeMedidaBase, custo);
    }

    // Custo só fecha se TODA linha tem custo: total parcial pareceria barato demais.
    private static (decimal? Total, decimal? PorRendimento) Custos(IReadOnlyList<LinhaDaReceita> linhas, decimal rendimento)
    {
        if (linhas.Count == 0 || linhas.Any(l => l.Custo is null)) return (null, null);
        var total = Math.Round(linhas.Sum(l => l.Custo!.Value), 2, MidpointRounding.AwayFromZero);
        return (total, rendimento > 0 ? Math.Round(total / rendimento, 2, MidpointRounding.AwayFromZero) : null);
    }
}
