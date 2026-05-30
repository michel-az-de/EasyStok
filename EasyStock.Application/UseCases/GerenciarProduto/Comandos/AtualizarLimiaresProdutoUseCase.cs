namespace EasyStock.Application.UseCases.GerenciarProduto.Comandos;

/// <summary>
/// Comando: atualiza as quantidades minima e critica de estoque de um produto.
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9). O facade
/// continua expondo <c>AtualizarLimiaresAsync</c> via delega, preservando
/// contrato publico (R8).
/// </summary>
public sealed class AtualizarLimiaresProdutoUseCase(
    IProdutoRepository produtoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid produtoId, int? quantidadeMinima, int? quantidadeCritica)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        if (quantidadeMinima.HasValue && quantidadeMinima.Value < 0)
            throw new UseCaseValidationException("Quantidade minima nao pode ser negativa.");
        if (quantidadeCritica.HasValue && quantidadeCritica.Value < 0)
            throw new UseCaseValidationException("Quantidade critica nao pode ser negativa.");
        if (quantidadeMinima.HasValue && quantidadeCritica.HasValue && quantidadeCritica.Value >= quantidadeMinima.Value)
            throw new UseCaseValidationException("Quantidade critica precisa ser menor que a minima.");

        produto.QuantidadeMinima = quantidadeMinima;
        produto.QuantidadeCritica = quantidadeCritica;
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();
    }
}
