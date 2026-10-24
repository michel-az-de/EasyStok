using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.UseCases.Acervo;

/// <summary>
/// #1808: a imagem aprovada do banco de imagens pode ir para qualquer prato da mesma empresa. Publicar
/// num prato ainda não vinculado acrescenta o vínculo; prato de outra empresa continua recusado.
/// </summary>
public class AcervoUsarEmPratoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly byte[] Original = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private readonly IAcervoRepository _repo = Substitute.For<IAcervoRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor _processor = Substitute.For<IImageProcessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly CardapioItem _vinculado = CardapioItem.CriarAvulso(Guid.NewGuid(), "Lasanha", 30m, null);
    private readonly CardapioItem _outroPrato = CardapioItem.CriarAvulso(Guid.NewGuid(), "Ravioli", 30m, null);
    private readonly AcervoImagem _imagem;

    public AcervoUsarEmPratoTests()
    {
        _imagem = new AcervoImagem
        {
            EmpresaId = Empresa, CriadoPorId = Autor, Titulo = "Foto", Origem = "Fotografia", Formato = "1:1",
            ContentType = "image/png", StorageKey = "acervo/original.png", Tamanho = Original.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(Original)),
            ItensJson = JsonSerializer.Serialize(new[] { _vinculado.Id }), AprovadoEm = DateTime.UtcNow, AprovadoPorId = Autor,
        };
        _repo.ObterAsync(Empresa, _imagem.Id, Arg.Any<CancellationToken>()).Returns(_imagem);
        _repo.ItemAsync(Empresa, _vinculado.Id, Arg.Any<CancellationToken>()).Returns(_vinculado);
        _repo.ItemAsync(Empresa, _outroPrato.Id, Arg.Any<CancellationToken>()).Returns(_outroPrato);
        _storage.DownloadAsync(_imagem.StorageKey, Arg.Any<CancellationToken>()).Returns(Original);
        _processor.Optimize(Original, "image/png", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns((new byte[] { 9, 9 }, "image/webp", ".webp"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new StoredFileResult("chave", "/files/" + c.Arg<FileUploadRequest>().FileName, "image/webp", 2));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<AcervoPublicacao>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<AcervoPublicacao>>>()(CancellationToken.None));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<AcervoImagem>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<AcervoImagem>>>()(CancellationToken.None));
    }

    private readonly IStorefrontRepository _vitrines = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioPublicoCache _publico = Substitute.For<ICardapioPublicoCache>();

    private AcervoUseCase Sut() => new(_repo, _storage, _processor, _uow, _vitrines, _publico);

    private Guid[] Vinculos() => JsonSerializer.Deserialize<Guid[]>(_imagem.ItensJson)!;

    [Fact]
    public async Task Publicar_EmPratoAindaNaoVinculado_DaMesmaEmpresa_LigaEPublica()
    {
        var publicacao = await Sut().PublicarAsync(Empresa, Autor, _imagem.Id, new PublicarImagem(Guid.NewGuid(), _outroPrato.Id, false, null), default);

        publicacao.ItemId.Should().Be(_outroPrato.Id);
        _outroPrato.FotoUrl.Should().Be(publicacao.Url);
        Vinculos().Should().BeEquivalentTo([_vinculado.Id, _outroPrato.Id]);
        await _repo.Received().BloquearImagemAsync(Empresa, _imagem.Id, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    // #2002: a foto publicada aparece no menu público na hora.
    [Fact]
    public async Task Publicar_InvalidaOMenuPublicoDaVitrineDoPrato()
    {
        var vitrine = EasyStock.Domain.Entities.Storefront.Storefront.Criar(Empresa, "casa-da-baba", "Casa da Baba", 0m);
        _vitrines.GetByIdAsync(_outroPrato.StorefrontId).Returns(vitrine);

        await Sut().PublicarAsync(Empresa, Autor, _imagem.Id, new PublicarImagem(Guid.NewGuid(), _outroPrato.Id, false, null), default);

        _publico.Received(1).Invalidar("casa-da-baba");
    }

    [Fact]
    public async Task Publicar_EmPratoDeOutraEmpresa_Recusa_SemLigarNemGravar()
    {
        var pratoAlheio = Guid.NewGuid();

        var act = () => Sut().PublicarAsync(Empresa, Autor, _imagem.Id, new PublicarImagem(Guid.NewGuid(), pratoAlheio, false, null), default);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("Item não encontrado.");
        Vinculos().Should().Equal(_vinculado.Id);
        await _storage.DidNotReceive().UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Adicionar_OMesmoArquivoDepoisDeUsarEmOutroPrato_DevolveAMesmaImagem()
    {
        _imagem.ItensJson = JsonSerializer.Serialize(new[] { _vinculado.Id, _outroPrato.Id });
        _repo.PorHashAsync(Empresa, _imagem.Sha256, Arg.Any<CancellationToken>()).Returns(_imagem);

        var repetida = await Sut().AdicionarAsync(Empresa, Autor, new DadosImagem("Foto", "Fotografia", "1:1", [_vinculado.Id]), "image/png", Original, default);

        repetida.Should().BeSameAs(_imagem, "reenviar o upload original não pode falhar só porque a imagem ganhou outro prato");
        Vinculos().Should().BeEquivalentTo([_vinculado.Id, _outroPrato.Id]);
    }
}
