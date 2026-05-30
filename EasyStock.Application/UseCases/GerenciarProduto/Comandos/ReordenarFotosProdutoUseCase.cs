using EasyStock.Application.UseCases.GerenciarProduto.Helpers;

namespace EasyStock.Application.UseCases.GerenciarProduto.Comandos;

/// <summary>
/// Comando: reordena as fotos do produto conforme array de IDs informado. Fotos
/// nao listadas no array nao sao perdidas — vao pro final da lista. Invalida
/// cache de relacionadas.
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9). O facade
/// continua expondo <c>ReordenarFotosAsync</c> via delegacao, preservando
/// contrato publico (R8).
/// </summary>
public sealed class ReordenarFotosProdutoUseCase(
    IProdutoRepository produtoRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null)
{
    public async Task ExecuteAsync(Guid empresaId, Guid produtoId, Guid[] novaOrdem)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var fotos = ProdutoFotosSerializer.Deserializar(produto.FotosJson).ToList();
        var reordenadas = novaOrdem
            .Select(id => fotos.FirstOrDefault(f => f.FotoId == id))
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        // Garante que fotos ausentes da lista não sejam perdidas
        reordenadas.AddRange(fotos.Where(f => !novaOrdem.Contains(f.FotoId)));

        produto.FotosJson = ProdutoFotosSerializer.Serializar(reordenadas);
        produto.AlteradoEm = DateTime.UtcNow;
        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
    }
}
