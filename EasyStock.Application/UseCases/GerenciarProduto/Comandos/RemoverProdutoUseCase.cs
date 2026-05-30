namespace EasyStock.Application.UseCases.GerenciarProduto.Comandos;

/// <summary>
/// Comando: marca um produto como Inativo (soft-delete). Valida que nao ha estoque
/// remanescente nem pedidos abertos referenciando o produto. Registra evento de
/// alteracao se usuario fornecido. Invalida cache de relacionadas.
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9). O facade continua
/// expondo <c>RemoverAsync</c> via delegacao, preservando contrato publico (R8).
/// </summary>
public sealed class RemoverProdutoUseCase(
    IProdutoRepository produtoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null,
    IProdutoAlteracaoRepository? alteracaoRepository = null,
    IPedidoRepository? pedidoRepository = null)
{
    public async Task ExecuteAsync(Guid empresaId, Guid produtoId, Guid usuarioId = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        if (await itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produtoId))
            throw new UseCaseValidationException("Nao e permitido inativar produto com estoque disponivel.");

        // Bloqueia inativação enquanto há pedido aberto (aguardando/preparando/pronto)
        // referenciando este produto — evita orfanar itens em produção.
        if (pedidoRepository is not null &&
            await pedidoRepository.ExistemPedidosAbertosComProdutoAsync(empresaId, produtoId))
        {
            throw new UseCaseValidationException(
                "Nao e permitido inativar produto com pedidos abertos. Finalize ou cancele os pedidos primeiro.");
        }

        produto.Status = StatusProduto.Inativo;
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
                Acao = "inativado",
                AlteradoEm = DateTime.UtcNow
            });
        }

        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
    }
}
