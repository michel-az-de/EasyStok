using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;

namespace EasyStock.Application.UseCases.GerenciarUploads;

public sealed record UploadedFileResult(
    string Url,
    string FileName,
    string ContentType,
    long Size,
    Guid? FileId = null);

public sealed class GerenciarUploadsUseCase(
    IFileStorage fileStorage,
    IProdutoRepository produtoRepository,
    IUsuarioRepository usuarioRepository,
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public async Task<UploadedFileResult> UploadFotoProdutoAsync(Guid empresaId, Guid produtoId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 5 * 1024 * 1024);

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var fotos = GerenciarProdutoUseCase.DesserializarFotos(produto.FotosJson).ToList();
        if (fotos.Count >= 5)
            throw new UseCaseValidationException("O produto ja possui o limite maximo de 5 fotos.");

        var extensao = Path.GetExtension(fileName);
        var fotoId = Guid.NewGuid();
        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"produtos/{empresaId}/{produtoId}",
                $"{fotoId}{extensao}",
                contentType,
                content),
            cancellationToken);

        fotos.Add(new ProdutoFotoMetadata(fotoId, stored.Url, stored.StorageKey, DateTime.UtcNow));
        produto.FotosJson = GerenciarProdutoUseCase.SerializarFotos(fotos);
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        return new UploadedFileResult(stored.Url, fileName, contentType, stored.Size, fotoId);
    }

    public async Task RemoverFotoProdutoAsync(Guid empresaId, Guid produtoId, Guid fotoId, CancellationToken cancellationToken = default)
    {
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var fotos = GerenciarProdutoUseCase.DesserializarFotos(produto.FotosJson).ToList();
        var foto = fotos.FirstOrDefault(f => f.FotoId == fotoId)
            ?? throw new UseCaseValidationException("Foto nao encontrada.");

        if (!string.IsNullOrWhiteSpace(foto.StorageKey))
            await fileStorage.DeleteAsync(foto.StorageKey, cancellationToken);

        fotos.Remove(foto);
        produto.FotosJson = GerenciarProdutoUseCase.SerializarFotos(fotos);
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();
    }

    public async Task<UploadedFileResult> UploadAvatarUsuarioAsync(Guid usuarioId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 2 * 1024 * 1024);

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new UseCaseValidationException("Usuario nao encontrado.");

        var extensao = Path.GetExtension(fileName);
        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"usuarios/{usuarioId}/avatar",
                $"{Guid.NewGuid()}{extensao}",
                contentType,
                content),
            cancellationToken);

        usuario.AvatarUrl = stored.Url;
        usuario.AlteradoEm = DateTime.UtcNow;

        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        return new UploadedFileResult(stored.Url, fileName, contentType, stored.Size);
    }

    public async Task<UploadedFileResult> UploadLogoLojaAsync(Guid empresaId, Guid lojaId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ValidarImagem(fileName, contentType, content, 2 * 1024 * 1024);

        var loja = await lojaRepository.GetByIdAsync(lojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        if (loja.EmpresaId != empresaId)
            throw new UseCaseValidationException("A loja informada nao pertence a empresa.");

        var extensao = Path.GetExtension(fileName);
        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                $"lojas/{empresaId}/{lojaId}/logo",
                $"{Guid.NewGuid()}{extensao}",
                contentType,
                content),
            cancellationToken);

        loja.LogoUrl = stored.Url;
        loja.AlteradoEm = DateTime.UtcNow;

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        return new UploadedFileResult(stored.Url, fileName, contentType, stored.Size);
    }

    private static void ValidarImagem(string fileName, string contentType, byte[] content, int maxSize)
    {
        if (content.Length == 0)
            throw new UseCaseValidationException("Arquivo vazio.");

        if (content.Length > maxSize)
            throw new UseCaseValidationException("Arquivo excede o tamanho permitido.");

        if (!AllowedImageTypes.Contains(contentType))
            throw new UseCaseValidationException("Formato de arquivo nao suportado.");

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            throw new UseCaseValidationException("Arquivo sem extensao valida.");
    }
}
