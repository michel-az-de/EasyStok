using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="Saldo">Na unidade base, de lotes com saldo e não vencidos.</param>
/// <param name="Receitas">Em quantas receitas o insumo entra (onde é usado).</param>
public sealed record InsumoDaProducao(
    Guid ProdutoId, string Nome, UnidadeMedida Unidade, decimal Saldo, int? Minimo, decimal? Custo, int Receitas,
    bool AbaixoDoMinimo);

public sealed record InsumoInput(string? Nome, UnidadeMedida? Unidade, int? Minimo, decimal? Custo);

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
    IUnitOfWork unitOfWork)
{
    public const string CategoriaInsumos = "Insumos";

    public async Task<IReadOnlyList<InsumoDaProducao>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var insumos = await produtoRepository.GetInsumosAsync(empresaId, ct);
        if (insumos.Count == 0) return [];
        var saldos = await itemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(empresaId, insumos.Select(i => i.Id).ToList(), ct);
        var receitas = await composicaoRepository.ContarReceitasPorInsumoAsync(empresaId, ct);
        return insumos.Select(i =>
        {
            var saldo = saldos.GetValueOrDefault(i.Id);
            return new InsumoDaProducao(
                i.Id, i.Nome, i.UnidadeMedidaBase, saldo, i.QuantidadeMinima, i.CustoReferencia?.Valor,
                receitas.GetValueOrDefault(i.Id), i.QuantidadeMinima is { } minimo && saldo < minimo);
        }).ToList();
    }

    /// <summary>Cadastro rápido: nome, unidade, mínimo e custo. O resto do produto fica no padrão.</summary>
    public async Task<Guid> CriarAsync(Guid empresaId, Guid usuarioId, InsumoInput dados, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new UseCaseValidationException("Informe o nome do insumo.");
        Validar(dados);

        var categoriaId = await CategoriaDeEstoque.ObterOuCriarAsync(categoriaRepository, unitOfWork, empresaId,
            CategoriaInsumos, "Insumos da produção (criada pelo console)");
        var r = await cadastrarProduto.ExecuteAsync(new CadastrarProdutoCommand(
            empresaId, categoriaId, null, dados.Nome.Trim(), null, null, TipoProduto.Alimento,
            null, null, true, null, dados.Custo, null, null, null, null, null, null, null, usuarioId));

        var produto = await produtoRepository.GetByIdAsync(empresaId, r.ProdutoId)
            ?? throw new UseCaseValidationException("Insumo não encontrado depois de criar.");
        produto.EhInsumo = true;
        produto.UnidadeMedidaBase = dados.Unidade ?? UnidadeMedida.Un;
        produto.QuantidadeMinima = dados.Minimo;
        produto.AlteradoEm = DateTime.UtcNow;
        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();
        return produto.Id;
    }

    /// <summary>Ajusta mínimo e custo (null = não mexe). Só insumo da empresa.</summary>
    public async Task AtualizarAsync(Guid empresaId, Guid produtoId, InsumoInput dados, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        Validar(dados);
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId);
        if (produto is null || !produto.EhInsumo) throw new UseCaseValidationException("Insumo não encontrado.");

        if (dados.Minimo.HasValue) produto.QuantidadeMinima = dados.Minimo;
        if (dados.Custo.HasValue) produto.CustoReferencia = Dinheiro.FromDecimal(dados.Custo.Value);
        if (dados.Unidade.HasValue) produto.UnidadeMedidaBase = dados.Unidade.Value;
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
