using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.GerenciarUploads;

public sealed record UploadedFileResult(
    string Url,
    string FileName,
    string ContentType,
    long Size,
    Guid? FileId = null);

public sealed class GerenciarUploadsUseCase(
    IFileStorage fileStorage,
    IImageProcessor imageProcessor,
    IProdutoRepository produtoRepository,
    IUsuarioRepository usuarioRepository,
    ILojaRepository lojaRepository,
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null)
{
    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public async Task<UploadedFileResult> UploadFotoProdutoAsync(Guid empresaId, Guid produtoId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 10 * 1024 * 1024); // aceita ate 10MB antes de otimizar

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var fotos = EasyStock.Application.UseCases.GerenciarProduto.Helpers.ProdutoFotosSerializer.Deserializar(produto.FotosJson).ToList();
        if (fotos.Count >= 5)
            throw new UseCaseValidationException("O produto ja possui o limite maximo de 5 fotos.");

        // Otimizar em thread de pool para não bloquear a thread async do request
        var (optimized, optContentType, optExt) = await Task.Run(
            () => imageProcessor.Optimize(content, contentType, maxSide: 1920, quality: 85),
            cancellationToken);

        var fotoId = Guid.NewGuid();
        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"produtos/{empresaId}/{produtoId}",
                $"{fotoId}{optExt}",
                optContentType,
                optimized),
            cancellationToken);

        fotos.Add(new ProdutoFotoMetadata(fotoId, stored.Url, stored.StorageKey, DateTime.UtcNow));
        produto.FotosJson = EasyStock.Application.UseCases.GerenciarProduto.Helpers.ProdutoFotosSerializer.Serializar(fotos);
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));

        return new UploadedFileResult(stored.Url, fileName, optContentType, stored.Size, fotoId);
    }

    public async Task RemoverFotoProdutoAsync(Guid empresaId, Guid produtoId, Guid fotoId, CancellationToken cancellationToken = default)
    {
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var fotos = EasyStock.Application.UseCases.GerenciarProduto.Helpers.ProdutoFotosSerializer.Deserializar(produto.FotosJson).ToList();
        var foto = fotos.FirstOrDefault(f => f.FotoId == fotoId)
            ?? throw new UseCaseValidationException("Foto nao encontrada.");

        if (!string.IsNullOrWhiteSpace(foto.StorageKey))
            await fileStorage.DeleteAsync(foto.StorageKey, cancellationToken);

        fotos.Remove(foto);
        produto.FotosJson = EasyStock.Application.UseCases.GerenciarProduto.Helpers.ProdutoFotosSerializer.Serializar(fotos);
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
    }

    public async Task<UploadedFileResult> UploadAvatarUsuarioAsync(Guid usuarioId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 5 * 1024 * 1024);

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new UseCaseValidationException("Usuario nao encontrado.");

        var (optimized, optContentType, optExt) = imageProcessor.Optimize(content, contentType, maxSide: 512, quality: 80);

        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"usuarios/{usuarioId}/avatar",
                $"{Guid.NewGuid()}{optExt}",
                optContentType,
                optimized),
            cancellationToken);

        // Best-effort: remove o avatar antigo após o novo estar persistido.
        // Fica como último passo para não bloquear o upload caso a delete falhe
        // (por exemplo, se a URL antiga for externa ou estiver inacessível).
        var urlAntigo = usuario.AvatarUrl;

        usuario.AvatarUrl = stored.Url;
        usuario.AlteradoEm = DateTime.UtcNow;

        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        await TryDeletePreviousAsync(urlAntigo, cancellationToken);

        return new UploadedFileResult(stored.Url, fileName, optContentType, stored.Size);
    }

    public async Task<UploadedFileResult> UploadLogoLojaAsync(Guid empresaId, Guid lojaId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 5 * 1024 * 1024);

        var loja = await lojaRepository.GetByIdAsync(lojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        if (loja.EmpresaId != empresaId)
            throw new UseCaseValidationException("A loja informada nao pertence a empresa.");

        var (optimized, optContentType, optExt) = imageProcessor.Optimize(content, contentType, maxSide: 512, quality: 80);

        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"lojas/{empresaId}/{lojaId}/logo",
                $"{Guid.NewGuid()}{optExt}",
                optContentType,
                optimized),
            cancellationToken);

        var logoAntigo = loja.LogoUrl;

        loja.LogoUrl = stored.Url;
        loja.AlteradoEm = DateTime.UtcNow;

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        await TryDeletePreviousAsync(logoAntigo, cancellationToken);

        return new UploadedFileResult(stored.Url, fileName, optContentType, stored.Size);
    }

    /// <summary>
    /// Foto de um item de cardápio da vitrine (ADR-0031). 1 foto por item: grava a nova
    /// com nome novo e remove a anterior best-effort (igual avatar/logo) — sem órfão no replace.
    /// Escopo de tenant fechado por construção: resolve o storefront pela empresa do token e
    /// busca o item via <c>GetByIdAndScopeAsync</c> (item de outra empresa → 404, não vaza).
    /// </summary>
    public async Task<UploadedFileResult> UploadFotoCardapioItemAsync(
        Guid empresaId, Guid itemId, string fileName, string contentType, byte[] content,
        CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 6 * 1024 * 1024); // ate 6MB antes de otimizar

        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, cancellationToken)
            ?? throw new UseCaseValidationException("Sua vitrine ainda nao foi criada.");

        var item = await cardapioItemRepository.GetByIdAndScopeAsync(storefront.Id, itemId, empresaId, cancellationToken)
            ?? throw new CardapioItemNaoEncontradoException(storefront.Id, itemId);

        var (optimized, optContentType, optExt) = await Task.Run(
            () => imageProcessor.Optimize(content, contentType, maxSide: 1920, quality: 85),
            cancellationToken);

        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"cardapios/{empresaId}/{storefront.Id}/{itemId}",
                $"{Guid.NewGuid()}{optExt}",
                optContentType,
                optimized),
            cancellationToken);

        var fotoAntiga = item.FotoUrl;
        item.AtualizarMetadata(fotoUrl: stored.Url);

        await cardapioItemRepository.UpdateAsync(item, cancellationToken);
        await unitOfWork.CommitAsync();

        // Best-effort: remove a foto anterior só depois de persistir a nova.
        await TryDeletePreviousAsync(fotoAntiga, cancellationToken);

        return new UploadedFileResult(stored.Url, fileName, optContentType, stored.Size);
    }

    /// <summary>
    /// Tenta remover um arquivo anteriormente referenciado, a partir da URL
    /// armazenada. Suporta URLs relativas locais (<c>/files/...</c>) e URLs
    /// absolutas (<c>https://.../bucket/path</c>). Qualquer falha é silenciada
    /// para não bloquear o fluxo de upload — limpeza residual pode ser feita
    /// por um job de varredura de órfãos.
    /// </summary>
    private async Task TryDeletePreviousAsync(string? urlAntigo, CancellationToken cancellationToken)
    {
        var storageKey = StorageKeyExtractor.Extract(urlAntigo);
        if (storageKey is null) return;

        try
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
        }
        catch
        {
            // Silencioso: limpeza residual fica a cargo de job de GC de arquivos órfãos.
        }
    }

    private static void ValidarImagem(string fileName, string contentType, byte[] content, int maxSize)
    {
        if (content.Length == 0)
            throw new UseCaseValidationException("Arquivo vazio.");

        if (content.Length > maxSize)
            throw new UseCaseValidationException("Arquivo excede o tamanho permitido.");

        if (!AllowedImageTypes.Contains(contentType))
            throw new UseCaseValidationException("Formato de arquivo não suportado.");

        // Confere a assinatura de bytes contra o tipo declarado (anti arquivo renomeado).
        // SkiaSharp decodifica depois como segundo gate para imagens.
        UploadSecurityValidator.EnsureContentMatchesDeclaredType(content, contentType);

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            throw new UseCaseValidationException("Arquivo sem extensao valida.");
    }
}
