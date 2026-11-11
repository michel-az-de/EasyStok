using System.Reflection;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ImpressaoLoja;
using EasyStock.Domain.Entities.Mobile;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2209: <c>GET api/mobile/impressao</c>. Com <c>Mobile:RequireApiKey=false</c> o filtro deixa passar a requisição sem
/// chave e sem aparelho; o controller responde 401 nesse caso, e a loja lida é sempre a do aparelho.
/// </summary>
public sealed class MobileImpressaoControllerTests
{
    private readonly IRegraImpressaoLojaRepository _repo = Substitute.For<IRegraImpressaoLojaRepository>();

    private MobileImpressaoController Controller(MobileDevice? device)
    {
        var c = new MobileImpressaoController(new ObterRegraImpressaoLojaUseCase(_repo))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (device is not null) c.HttpContext.Items[MobileAuth.HttpContextItemDevice] = device;
        return c;
    }

    [Fact]
    public async Task SemAparelho_401_SemLerRegra()
    {
        var result = await Controller(null).Obter(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
    }

    [Fact]
    public async Task ComAparelho_LeARegraDaEmpresaEDaLojaDoPareamento()
    {
        var device = new MobileDevice { Id = "dev-1", ApiKeyHash = "h", EmpresaId = Guid.NewGuid(), LojaId = Guid.NewGuid() };
        _repo.ObterAsync(device.EmpresaId, device.LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(null, 0, null));

        var controller = Controller(device);
        var result = await controller.Obter(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"0\"");
        await _repo.Received(1).ObterAsync(device.EmpresaId, device.LojaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LojaDoPareamentoForaDaEmpresa_404()
    {
        var device = new MobileDevice { Id = "dev-1", ApiKeyHash = "h", EmpresaId = Guid.NewGuid(), LojaId = Guid.NewGuid() };
        _repo.ObterAsync(device.EmpresaId, device.LojaId, Arg.Any<CancellationToken>()).Returns((RegraImpressaoSalva?)null);

        (await Controller(device).Obter(CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Rota_SoAceitaGet_ComFiltroDeAparelho_ESemParametroDeLoja()
    {
        var tipo = typeof(MobileImpressaoController);
        tipo.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/mobile/impressao");
        tipo.GetCustomAttribute<MobileApiKeyAttribute>().Should().NotBeNull();
        tipo.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull("o PWA não tem JWT; a chave do aparelho autentica");

        var acoes = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        acoes.Should().ContainSingle("o PUT fica só no Console e na Web");
        acoes[0].GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        acoes[0].GetParameters().Select(p => p.ParameterType).Should().Equal([typeof(CancellationToken)],
            "a loja vem do pareamento: nenhum parâmetro escolhe loja ou empresa");
    }

    [Fact]
    public void Modulos_ContratoDoAparelho_ClassificadoSemExigirJwt()
    {
        var tipo = typeof(MobileImpressaoController);
        EasyStock.Api.Authorization.ModulosConvention.Classificado(tipo).Should().BeTrue("a convenção derruba o boot com controller sem classificação");
        EasyStock.Api.Authorization.ModulosConvention.ModulosDe(tipo, nameof(MobileImpressaoController.Obter))
            .Should().BeNull("o aparelho se autentica pela chave, sem usuário do Console");
    }
}
