using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="Saldo">Na unidade base, de lotes com saldo e não vencidos.</param>
/// <param name="Receitas">Em quantas receitas o insumo entra (onde é usado).</param>
/// <param name="Porcoes">M1.4e (#1552): em quantas porções do cardápio o insumo é a embalagem escolhida.</param>
public sealed record InsumoDaProducao(
    Guid ProdutoId, string Nome, UnidadeMedida Unidade, decimal Saldo, int? Minimo, decimal? Custo, int Receitas,
    bool AbaixoDoMinimo, bool Embalagem = false, int Porcoes = 0);

/// <param name="Embalagem">D-M2-03 (#1523): bandeja, selo, pote. Null = não mexe (no cadastro, não é).</param>
/// <param name="LimparMinimo">#1563: no ajuste, grava o mínimo nulo (volta a herdar da categoria e da loja). Mínimo null sozinho continua "não mexe".</param>
public sealed record InsumoInput(string? Nome, UnidadeMedida? Unidade, int? Minimo, decimal? Custo, bool? Embalagem = null,
    bool LimparMinimo = false);

/// <summary>
/// Insumos da produção pelo console (M2.3, #1496): o intermediário (molho, recheio, massa laminada)
/// e a embalagem; farinha e ovo ficam fora (US-067). Insumo é um produto com <c>EhInsumo</c>. Abaixo
/// do mínimo vai para "Comprar" (alimenta o planejamento, M2.5, e a US-066).
/// </summary>
public sealed class InsumosDaProducaoUseCase(
    IProdutoRepository produtoRepository,
    IProdutoComposicaoRepository composicaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ICategoriaRepository categoriaRepository,
    CadastrarProdutoUseCase cadastrarProduto,
    IUnitOfWork unitOfWork,
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository)
{
    public const string CategoriaInsumos = "Insumos";
    public const string CategoriaEmbalagens = "Embalagens";

    public async Task<IReadOnlyList<InsumoDaProducao>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var insumos = await produtoRepository.GetInsumosAsync(empresaId, ct);
        if (insumos.Count == 0) return [];
        var saldos = await itemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(empresaId, insumos.Select(i => i.Id).ToList(), ct);
        var receitas = await composicaoRepository.ContarReceitasPorInsumoAsync(empresaId, ct);
        var porcoes = await PorcoesPorEmbalagemAsync(empresaId, ct);
        return insumos.Select(i =>
        {
            var saldo = saldos.GetValueOrDefault(i.Id);
            return new InsumoDaProducao(
                i.Id, i.Nome, i.UnidadeMedidaBase, saldo, i.QuantidadeMinima, i.CustoReferencia?.Valor,
                receitas.GetValueOrDefault(i.Id), i.QuantidadeMinima is { } minimo && saldo < minimo, i.EhEmbalagem,
                porcoes.GetValueOrDefault(i.Id));
        }).ToList();
    }

    // M1.4e (#1552): a embalagem escolhida numa porção não entra em receita, mas está em uso. Prato tirado do cardápio não conta.
    private async Task<Dictionary<Guid, int>> PorcoesPorEmbalagemAsync(Guid empresaId, CancellationToken ct)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null) return [];
        return (await cardapioRepository.GetTodosDoStorefrontAsync(storefront.Id, ct))
            .Where(i => !i.EstaArquivado)
            .SelectMany(i => i.Variacoes)
            .Where(v => v.EmbalagemProdutoId.HasValue)
            .GroupBy(v => v.EmbalagemProdutoId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Cadastro rápido: nome, unidade, mínimo e custo. O resto do produto fica no padrão.</summary>
    public async Task<Guid> CriarAsync(Guid empresaId, Guid usuarioId, InsumoInput dados, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new UseCaseValidationException("Informe o nome do insumo.");
        Validar(dados);

        var embalagem = dados.Embalagem == true;
        var categoriaId = embalagem
            ? await CategoriaDeEstoque.ObterOuCriarAsync(categoriaRepository, unitOfWork, empresaId,
                CategoriaEmbalagens, "Bandejas, selos e potes da produção (criada pelo console)")
            : await CategoriaDeEstoque.ObterOuCriarAsync(categoriaRepository, unitOfWork, empresaId,
                CategoriaInsumos, "Insumos da produção (criada pelo console)");
        var r = await cadastrarProduto.ExecuteAsync(new CadastrarProdutoCommand(
            empresaId, categoriaId, null, dados.Nome.Trim(), null, null, TipoProduto.Alimento,
            null, null, true, null, dados.Custo, null, null, null, null, null, null, null, usuarioId,
            EhInsumo: true, UnidadeMedidaBase: dados.Unidade ?? UnidadeMedida.Un, QuantidadeMinima: dados.Minimo,
            EhEmbalagem: embalagem));
        return r.ProdutoId;
    }

    /// <summary>Ajusta mínimo e custo (null = não mexe; <c>LimparMinimo</c> apaga o mínimo). Só insumo da empresa.</summary>
    public async Task AtualizarAsync(Guid empresaId, Guid produtoId, InsumoInput dados, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        Validar(dados);
        if (dados.LimparMinimo && dados.Minimo.HasValue)
            throw new UseCaseValidationException("Informe o mínimo ou limpe o mínimo, não os dois.");
        var produto = await produtoRepository.GetByIdParaAtualizarAsync(empresaId, produtoId);
        if (produto is null || produto.EmpresaId != empresaId || !produto.EhInsumo) throw new UseCaseValidationException("Insumo não encontrado.");

        // #1563: zero não serve para limpar, porque vence a herança da categoria e da loja (LimiarEstoqueResolver).
        if (dados.LimparMinimo) produto.QuantidadeMinima = null;
        else if (dados.Minimo.HasValue) produto.QuantidadeMinima = dados.Minimo;
        if (dados.Custo.HasValue) produto.CustoReferencia = Dinheiro.FromDecimal(dados.Custo.Value);
        if (dados.Unidade.HasValue) produto.UnidadeMedidaBase = dados.Unidade.Value;
        if (dados.Embalagem.HasValue) produto.EhEmbalagem = dados.Embalagem.Value;
        produto.AlteradoEm = DateTime.UtcNow;
        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();
    }

    private static void Validar(InsumoInput dados)
    {
        if (dados.Minimo is < 0) throw new UseCaseValidationException("O mínimo não pode ser negativo.");
        if (dados.Custo is < 0) throw new UseCaseValidationException("O custo não pode ser negativo.");
    }
}
