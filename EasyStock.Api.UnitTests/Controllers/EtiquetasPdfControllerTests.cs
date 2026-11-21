using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2200: <c>POST api/etiquetas/pdf</c> devolve o PDF da folha ou 400 com o envelope de erro do projeto.</summary>
public class EtiquetasPdfControllerTests
{
    private static readonly byte[] PdfFalso = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF");
    private readonly IFolhaEtiquetasPdfRenderer _renderer = Substitute.For<IFolhaEtiquetasPdfRenderer>();
    private readonly EtiquetasPdfController _controller;

    public EtiquetasPdfControllerTests()
    {
        _renderer.RenderAsync(Arg.Any<PlanoFolhaEtiquetas>(), Arg.Any<CancellationToken>()).Returns(PdfFalso);
        _controller = Controller(new VagasDeFolhaPdf());
    }

    private EtiquetasPdfController Controller(VagasDeFolhaPdf vagas) =>
        new(new GerarFolhaEtiquetasPdfUseCase(_renderer, Substitute.For<IQrCodeEncoder>(), vagas))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task Gerar_SemVaga_Devolve503ComRetryAfterSemRenderizar()
    {
        var vagas = new VagasDeFolhaPdf(1, TimeSpan.FromMilliseconds(20));
        using var ocupada = await vagas.OcuparAsync();
        var controller = Controller(vagas);

        var resultado = await controller.Gerar(Pedido(Etiqueta()), CancellationToken.None);

        var objeto = resultado.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        objeto.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Code.Should().Be(EtiquetasPdfController.CodigoOcupado);
        controller.Response.Headers.RetryAfter.ToString().Should().Be(FolhaPdfOcupadaException.TenteDeNovoEmSegundos.ToString());
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
    }

    private static EtiquetaNaFolha Etiqueta(decimal nomePt = 10m) => new(JsonSerializer.SerializeToElement(new JsonObject
    {
        ["v"] = 1,
        ["size"] = new JsonObject { ["preset"] = "custom", ["w_mm"] = 63.5m, ["h_mm"] = 38.1m, ["orientation"] = "horizontal" },
        ["elements"] = new JsonArray(new JsonObject
        {
            ["id"] = "nome", ["type"] = "text", ["content"] = "Talharim", ["font"] = "sans", ["size_pt"] = nomePt,
            ["weight"] = 700, ["align"] = "left", ["overflow"] = "clip",
            ["x_mm"] = 1.5m, ["y_mm"] = 5m, ["w_mm"] = 41m, ["h_mm"] = 4m,
        }),
    }), null);

    private static GerarFolhaEtiquetasPdfCommand Pedido(params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("pimaco-a4256", null, null, null), Guias: false, Pular: 0, etiquetas);

    [Fact]
    public async Task Gerar_EntradaValida_Devolve200ComBytesPdf()
    {
        var resultado = await _controller.Gerar(Pedido(Etiqueta()), CancellationToken.None);

        var arquivo = resultado.Should().BeOfType<FileContentResult>().Subject;
        arquivo.ContentType.Should().Be("application/pdf");
        arquivo.FileContents.Should().Equal(PdfFalso);
        _controller.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    [Fact]
    public async Task Gerar_SemEtiquetas_Devolve400NoEnvelopeDoProjeto()
    {
        var resultado = await _controller.Gerar(Pedido(), CancellationToken.None);

        var erro = resultado.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        erro.Error.Code.Should().Be(GerarFolhaEtiquetasPdfUseCase.CodigoErro);
        erro.Error.Message.Should().Contain("etiquetas");
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
    }

    [Fact]
    public async Task Gerar_FonteAbaixoDoPiso_Devolve400SemRenderizar()
    {
        var resultado = await _controller.Gerar(Pedido(Etiqueta(nomePt: 8m)), CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
    }

    [Fact]
    public void Rota_ExigeAutenticacaoEPoliticaOperador()
    {
        var tipo = typeof(EtiquetasPdfController);
        tipo.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        tipo.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/etiquetas/pdf");
        var acao = tipo.GetMethod(nameof(EtiquetasPdfController.Gerar))!;
        acao.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        acao.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("Operador");
        acao.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public void Controller_EstaClassificadoNoModuloProducao()
    {
        ModulosConvention.Classificado(typeof(EtiquetasPdfController)).Should().BeTrue();
        ModulosConvention.ModulosDe(typeof(EtiquetasPdfController), nameof(EtiquetasPdfController.Gerar))
            .Should().Equal(Modulo.Producao);
    }

    [Fact]
    public void Corpo_UsaMargemEEspacoEmSnakeCaseComoOLayoutJson()
    {
        var corpo = JsonSerializer.Deserialize<GerarFolhaEtiquetasPdfCommand>(
            """{"folha":{"modelo":"quantas-cabem","margem_mm":6,"espaco_mm":2.5},"guias":true,"pular":3,"etiquetas":[]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        corpo.Folha!.MargemMm.Should().Be(6m);
        corpo.Folha.EspacoMm.Should().Be(2.5m);
        corpo.Guias.Should().BeTrue();
        corpo.Pular.Should().Be(3);
    }
}
