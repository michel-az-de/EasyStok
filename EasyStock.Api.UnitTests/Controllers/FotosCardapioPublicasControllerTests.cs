using System.Reflection;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1448: a foto do cardápio sai pela API por chave, de qualquer host gravado na URL, e só de
/// <c>cardapios/</c>. Nada de outra pasta do storage, nada de <c>..</c>.
/// </summary>
public class FotosCardapioPublicasControllerTests
{
    private const string Caminho = "empresa/vitrine/item/angulo.webp";
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly FotosCardapioPublicasController _controller;

    public FotosCardapioPublicasControllerTests()
    {
        _controller = new FotosCardapioPublicasController(new ObterFotoCardapioUseCase(_storage))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task FotoDoCardapio_SaiDoStorageComMimeECache()
    {
        _storage.ExistsAsync("cardapios/" + Caminho, Arg.Any<CancellationToken>()).Returns(true);
        _storage.DownloadAsync("cardapios/" + Caminho, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = await _controller.Obter(Caminho);

        var arquivo = result.Should().BeOfType<FileContentResult>().Subject;
        arquivo.ContentType.Should().Be("image/webp");
        arquivo.FileContents.Should().Equal(1, 2, 3);
        _controller.Response.Headers.CacheControl.ToString().Should().Contain("public").And.Contain("max-age=604800");
    }

    [Fact]
    public async Task ArquivoAusente_404()
    {
        _storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        (await _controller.Obter(Caminho)).Should().BeOfType<NotFoundObjectResult>();
        await _storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../atendimento/empresa/conversa/foto.jpg")]
    [InlineData("empresa/../../atendimento/foto.jpg")]
    [InlineData("empresa/./foto.jpg")]
    [InlineData("empresa//foto.jpg")]
    [InlineData("/empresa/foto.jpg")]
    [InlineData("empresa\\..\\foto.jpg")]
    [InlineData("empresa/relatorio.pdf")]
    [InlineData("empresa/sem-extensao")]
    [InlineData("c:/windows/foto.jpg")]
    public async Task CaminhoForaDoCardapio_404SemTocarNoStorage(string? caminho)
    {
        _storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        (await _controller.Obter(caminho)).Should().BeOfType<NotFoundObjectResult>();
        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    [Theory]
    [InlineData("a/b/c/foto.JPG", "image/jpeg")]
    [InlineData("a/b/c/foto.jpeg", "image/jpeg")]
    [InlineData("a/b/c/foto.png", "image/png")]
    [InlineData("a/b/c/foto.gif", "image/gif")]
    public void ChaveDoCaminho_SoFotoSobCardapios(string caminho, string _)
    {
        ObterFotoCardapioUseCase.ChaveDoCaminho(caminho).Should().Be("cardapios/" + caminho);
    }

    [Fact]
    public void CaminhoLongoDemais_Nulo()
    {
        ObterFotoCardapioUseCase.ChaveDoCaminho(new string('a', 500) + "/foto.jpg").Should().BeNull();
    }

    [Fact]
    public void Rota_EAnonimaEPublica()
    {
        typeof(FotosCardapioPublicasController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(FotosCardapioPublicasController).GetCustomAttribute<RouteAttribute>()!.Template
            .Should().Be("api/public/cardapio/fotos");
        typeof(FotosCardapioPublicasController).GetMethod(nameof(FotosCardapioPublicasController.Obter))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{**caminho}");
    }
}
