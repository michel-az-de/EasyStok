using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2027: a URL e a chave gravadas no produto, na loja e no usuário vêm do cliente (o cadastro do produto aceita
/// o <c>FotosJson</c> do corpo). Apontadas para o arquivo de outra empresa, a troca e a remoção apagavam o arquivo dela.
/// </summary>
public class ArquivoDeOutroDonoNaoSaiDoStorageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly string Alheia = $"cardapios/{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}/foto.webp";

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly ILojaRepository _lojas = Substitute.For<ILojaRepository>();
    private readonly GerenciarUploadsUseCase _useCase;

    public ArquivoDeOutroDonoNaoSaiDoStorageTests()
    {
        var imagens = Substitute.For<IImageProcessor>();
        imagens.Optimize(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(([1, 2, 3], "image/webp", ".webp"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("x/nova.webp", "/files/x/nova.webp", "image/webp", 3));
        _useCase = new GerenciarUploadsUseCase(
            _storage, imagens, _produtos, _usuarios, _lojas, Substitute.For<IStorefrontRepository>(),
            Substitute.For<ICardapioItemRepository>(), Substitute.For<IUnitOfWork>());
    }

    private Produto ProdutoComFoto(Guid fotoId, string chave)
    {
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Lasanha" };
        produto.FotosJson = $$"""[{"FotoId":"{{fotoId}}","Url":"/files/{{chave}}","StorageKey":"{{chave}}","CriadoEm":"2026-10-24T00:00:00Z"}]""";
        _produtos.GetByIdAsync(_empresa, produto.Id).Returns(produto);
        return produto;
    }

    [Fact]
    public async Task RemoverFotoDoProduto_NaoApagaChaveDeOutraEmpresa()
    {
        var fotoId = Guid.NewGuid();
        var produto = ProdutoComFoto(fotoId, Alheia);

        await _useCase.RemoverFotoProdutoAsync(_empresa, produto.Id, fotoId);

        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        produto.FotosJson.Should().NotContain(fotoId.ToString(), "a foto sai da lista mesmo sem apagar o arquivo alheio");
    }

    [Fact]
    public async Task RemoverFotoDoProduto_ApagaAFotoDele()
    {
        var fotoId = Guid.NewGuid();
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Lasanha" };
        var propria = $"produtos/{_empresa}/{produto.Id}/{fotoId}.webp";
        produto.FotosJson = $$"""[{"FotoId":"{{fotoId}}","Url":"/files/{{propria}}","StorageKey":"{{propria}}","CriadoEm":"2026-10-24T00:00:00Z"}]""";
        _produtos.GetByIdAsync(_empresa, produto.Id).Returns(produto);

        await _useCase.RemoverFotoProdutoAsync(_empresa, produto.Id, fotoId);

        await _storage.Received(1).DeleteAsync(propria, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrocarLogo_SoApagaOLogoAnteriorDaPropriaLoja()
    {
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Casa", LogoUrl = $"/files/{Alheia}" };
        _lojas.GetByIdAsync(loja.Id).Returns(loja);

        await _useCase.UploadLogoLojaAsync(_empresa, loja.Id, "logo.png", "image/png", Png);

        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);

        var anterior = $"lojas/{_empresa}/{loja.Id}/logo/antigo.webp";
        loja.LogoUrl = $"/files/{anterior}";
        await _useCase.UploadLogoLojaAsync(_empresa, loja.Id, "logo.png", "image/png", Png);

        await _storage.Received(1).DeleteAsync(anterior, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrocarAvatar_SoApagaOAvatarAnteriorDoProprioUsuario()
    {
        var usuario = new Usuario { Id = Guid.NewGuid(), AvatarUrl = $"/files/{Alheia}" };
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);

        await _useCase.UploadAvatarUsuarioAsync(usuario.Id, "a.png", "image/png", Png);

        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);

        var anterior = $"usuarios/{usuario.Id}/avatar/antigo.webp";
        usuario.AvatarUrl = $"/files/{anterior}";
        await _useCase.UploadAvatarUsuarioAsync(usuario.Id, "a.png", "image/png", Png);

        await _storage.Received(1).DeleteAsync(anterior, Arg.Any<CancellationToken>());
    }
}
