namespace EasyStock.Application.UseCases.GerenciarProduto.Comandos;

/// <summary>
/// Comando: re-ativa um produto previamente inativado (soft-undelete). Registra
/// evento de alteracao se usuario fornecido. Invalida cache de relacionadas.
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9). O facade continua
/// expondo <c>RestaurarAsync</c> via delegacao, preservando contrato publico (R8).
/// </summary>
public sealed class RestaurarProdutoUseCase(
    IProdutoRepository produtoRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null,
    IProdutoAlteracaoRepository? alteracaoRepository = null)
{
    public async Task ExecuteAsync(Guid empresaId, Guid produtoId, Guid usuarioId = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        produto.Status = StatusProduto.Ativo;
        produto.AlteradoPor = usuarioId != Guid.Empty ? usuarioId : null;
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);

        if (alteracaoRepository is not null && usuarioId != Guid.Empty)
        {
            await alteracaoRepository.AddAsync(new ProdutoAlteracao
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                UsuarioId = usuarioId,
                Acao = "restaurado",
                AlteradoEm = DateTime.UtcNow
            });
        }

        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
    }
}
