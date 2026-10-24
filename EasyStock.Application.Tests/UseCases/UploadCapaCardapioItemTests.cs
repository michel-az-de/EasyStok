using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #1550: trocar a capa de um prato cuja capa foi gravada fora da galeria (estado legado) não pode
/// descartar o primeiro ângulo, nem subir arquivo quando não há vaga para a capa nova.
/// </summary>
public class UploadCapaCardapioItemTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private const string Nova = "https://cdn.exemplo/nova.webp";
    private const string CapaLegada = "https://cdn.exemplo/capa-legada.webp";

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly CardapioItem _item;
    private readonly GerenciarUploadsUseCase _useCase;

    public UploadCapaCardapioItemTests()
    {
        var loja = StorefrontEntity.Criar(_empresa, "capa-legada", "Loja", 0);
        _item = CardapioItem.CriarAvulso(loja.Id, "Lasanha", 50m);
        var imagens = Substitute.For<IImageProcessor>();
        imagens.Optimize(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(([1, 2, 3], "image/webp", ".webp"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("cardapios/nova.webp", Nova, "image/webp", 3));
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetByEmpresaAsync(_empresa, Arg.Any<CancellationToken>()).Returns(loja);
        var itens = Substitute.For<ICardapioItemRepository>();
        itens.GetByIdAndScopeAsync(loja.Id, _item.Id, _empresa, Arg.Any<CancellationToken>()).Returns(_item);
        _useCase = new GerenciarUploadsUseCase(
            _storage, imagens, Substitute.For<IProdutoRepository>(), Substitute.For<IUsuarioRepository>(),
            Substitute.For<ILojaRepository>(), lojas, itens, _uow);
    }

    private Task TrocarCapa() => _useCase.UploadFotoCardapioItemAsync(
        _empresa, _item.Id, "capa.png", "image/png", Png, sincronizarCapaGaleria: true);

    private List<string> GaleriaComCapaLegada(int angulos)
    {
        var fotos = Enumerable.Range(1, angulos).Select(n => $"https://cdn.exemplo/{n}.webp").ToList();
        foreach (var foto in fotos) _item.AdicionarFotoGaleria(foto);
        _item.AtualizarMetadata(fotoUrl: CapaLegada);
        return fotos;
    }

    [Fact]
    public async Task CapaLegadaForaDaGaleria_ConservaTodosOsAngulos()
    {
        var angulos = GaleriaComCapaLegada(2);

        await TrocarCapa();

        _item.FotoUrl.Should().Be(Nova);
        _item.ObterFotosGaleria().Should().Equal(angulos.Prepend(Nova));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CapaLegadaComGaleriaCheia_RecusaAntesDeSubirOArquivo()
    {
        var angulos = GaleriaComCapaLegada(CardapioItem.LimiteFotosGaleria);

        var trocar = () => TrocarCapa();

        await trocar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*limite de 5 fotos*");
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
        _item.FotoUrl.Should().Be(CapaLegada);
        _item.ObterFotosGaleria().Should().Equal(angulos);
    }

    // #2027: trocar a capa apagava a anterior pela FotoUrl, mesmo que ela apontasse para o arquivo de outra empresa.
    [Fact]
    public async Task TrocarCapa_NaoApagaArquivoDeOutraEmpresa()
    {
        var alheia = $"cardapios/{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}/foto.webp";
        _item.AtualizarMetadata(fotoUrl: $"/files/{alheia}");

        await _useCase.UploadFotoCardapioItemAsync(_empresa, _item.Id, "capa.png", "image/png", Png);

        await _storage.DidNotReceive().DeleteAsync(alheia, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrocarCapa_ApagaACapaAnteriorDoProprioPrato()
    {
        var anterior = $"cardapios/{_empresa}/{_item.StorefrontId}/{_item.Id}/anterior.webp";
        _item.AtualizarMetadata(fotoUrl: $"/files/{anterior}");

        await _useCase.UploadFotoCardapioItemAsync(_empresa, _item.Id, "capa.png", "image/png", Png);

        await _storage.Received(1).DeleteAsync(anterior, Arg.Any<CancellationToken>());
    }
}
