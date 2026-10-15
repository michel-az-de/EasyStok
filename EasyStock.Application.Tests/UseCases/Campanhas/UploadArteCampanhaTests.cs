using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>S28: a arte da campanha sobe pelo mesmo caminho validado dos uploads, na pasta da empresa.</summary>
public class UploadArteCampanhaTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor _imagens = Substitute.For<IImageProcessor>();
    private readonly GerenciarUploadsUseCase _useCase;

    public UploadArteCampanhaTests()
    {
        _imagens.Optimize(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(([1, 2, 3], "image/webp", ".webp"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new StoredFileResult(
                $"{c.Arg<FileUploadRequest>().BucketPath}/{c.Arg<FileUploadRequest>().FileName}",
                "https://cdn.exemplo/arte.webp", "image/webp", 3));
        _useCase = new GerenciarUploadsUseCase(
            _storage, _imagens, Substitute.For<IProdutoRepository>(), Substitute.For<IUsuarioRepository>(),
            Substitute.For<ILojaRepository>(), Substitute.For<IStorefrontRepository>(),
            Substitute.For<ICardapioItemRepository>(), Substitute.For<IUnitOfWork>());
    }

    [Fact]
    public async Task GuardaNaPastaDaEmpresaEDevolveUrlPublica()
    {
        var empresaId = Guid.NewGuid();

        var resultado = await _useCase.UploadArteCampanhaAsync(empresaId, "arte.png", "image/png", Png);

        resultado.Url.Should().Be("https://cdn.exemplo/arte.webp");
        resultado.StorageKey.Should().StartWith($"campanhas/{empresaId}/");
        await _storage.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r => r.BucketPath == $"campanhas/{empresaId}" && r.IsPublic),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecusaArquivoQueNaoEImagem()
    {
        var enviar = () => _useCase.UploadArteCampanhaAsync(Guid.NewGuid(), "arte.png", "image/png", [1, 2, 3, 4]);

        // Contrato existente do UploadSecurityValidator: assinatura errada sobe InvalidOperationException.
        await enviar.Should().ThrowAsync<InvalidOperationException>();
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }
}
