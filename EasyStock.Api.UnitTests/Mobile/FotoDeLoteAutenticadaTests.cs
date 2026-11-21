using System.Reflection;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2459: a foto de lote mobile sai do <c>/files</c> público. O upload grava a URL da rota
/// <c>GET api/mobile/batches/{id}/photos/{arquivo}</c>, que exige o aparelho e só serve lote da empresa dele.
/// </summary>
public sealed class FotoDeLoteAutenticadaTests : IDisposable
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();

    public FotoDeLoteAutenticadaTests()
    {
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"foto-lote-{Guid.NewGuid()}").Options,
            Substitute.For<ICurrentUserAccessor>());
        _db.Set<Batch>().Add(new Batch { Id = "lote-1", Code = "LASA-1", EmpresaId = _empresa });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private MobileBatchesController Controller(Guid? empresaDoAparelho)
    {
        var c = new MobileBatchesController(_db, null!, null!, Substitute.For<ICurrentUserAccessor>(), _storage,
            NullLogger<MobileBatchesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (empresaDoAparelho is { } emp)
            c.HttpContext.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice { Id = "dev-1", ApiKeyHash = "h", EmpresaId = emp };
        return c;
    }

    [Fact]
    public async Task Upload_GravaUrlDaRotaAutenticada_NaoDoFilesPublico()
    {
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("mobile-photos/x/lote-1/abc123.jpg", "/files/mobile-photos/x/lote-1/abc123.jpg", "image/jpeg", 3));
        var foto = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary(), ContentType = "image/jpeg"
        };

        var result = await Controller(_empresa).UploadPhoto("lote-1", "abc123", "batchPhoto", foto, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var batch = await _db.Set<Batch>().AsNoTracking().SingleAsync(b => b.Id == "lote-1");
        batch.BatchPhoto.Should().Be("/api/mobile/batches/lote-1/photos/abc123.jpg");
    }

    [Fact]
    public async Task ObterFoto_SemAparelho_401_SemLerStorage()
    {
        var result = await Controller(null).ObterFoto("lote-1", "abc123.jpg", CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await _storage.DidNotReceiveWithAnyArgs().DownloadStreamAsync(default!, default);
    }

    [Fact]
    public async Task ObterFoto_AparelhoDeOutraEmpresa_404_SemLerStorage()
    {
        var result = await Controller(Guid.NewGuid()).ObterFoto("lote-1", "abc123.jpg", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().DownloadStreamAsync(default!, default);
    }

    [Fact]
    public async Task ObterFoto_AparelhoDaEmpresa_ServeAFotoDoLote()
    {
        var chave = $"mobile-photos/{_empresa}/lote-1/abc123.jpg";
        _storage.ExistsAsync(chave, Arg.Any<CancellationToken>()).Returns(true);
        _storage.DownloadStreamAsync(chave, Arg.Any<CancellationToken>()).Returns(new MemoryStream([1, 2, 3]));

        var controller = Controller(_empresa);
        var result = await controller.ObterFoto("lote-1", "abc123.jpg", CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>().Which.ContentType.Should().Be("image/jpeg");
        controller.Response.Headers.CacheControl.ToString().Should().Contain("private");
    }

    [Fact]
    public async Task ObterFoto_ArquivoAusente_404()
    {
        _storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Controller(_empresa).ObterFoto("lote-1", "abc123.png", CancellationToken.None))
            .Should().BeOfType<NotFoundResult>();
        await _storage.DidNotReceiveWithAnyArgs().DownloadStreamAsync(default!, default);
    }

    [Theory]
    [InlineData("../outro/abc.jpg")]
    [InlineData("..")]
    [InlineData("abc.svg")]
    [InlineData("abc")]
    public async Task ObterFoto_NomeForaDoPadrao_404_SemTocarStorage(string arquivo)
    {
        (await Controller(_empresa).ObterFoto("lote-1", arquivo, CancellationToken.None))
            .Should().BeOfType<NotFoundResult>();
        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
    }

    [Theory]
    [InlineData("../../clientes")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    public async Task IdDeLoteComSeparadorDeCaminho_404_SemTocarStorage(string id)
    {
        // O id vem do aparelho: com "../" a chave sairia da pasta da empresa.
        _db.Set<Batch>().Add(new Batch { Id = id, Code = "X", EmpresaId = _empresa });
        await _db.SaveChangesAsync();
        var foto = new FormFile(new MemoryStream([1]), 0, 1, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

        (await Controller(_empresa).ObterFoto(id, "abc123.jpg", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        (await Controller(_empresa).UploadPhoto(id, "abc123", "batchPhoto", foto, CancellationToken.None))
            .Should().BeAssignableTo<NotFoundObjectResult>();
        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public void Rota_GetComFiltroDoAparelho_TransversalComoOUpload()
    {
        var acao = typeof(MobileBatchesController).GetMethod(nameof(MobileBatchesController.ObterFoto))!;
        acao.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{id}/photos/{arquivo}");
        acao.GetCustomAttribute<MobileApiKeyAttribute>().Should().NotBeNull();
        acao.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull("o PWA não tem JWT; a chave do aparelho autentica");
        EasyStock.Api.Authorization.ModulosConvention.ModulosDe(typeof(MobileBatchesController), nameof(MobileBatchesController.ObterFoto))
            .Should().BeNull();
    }
}
