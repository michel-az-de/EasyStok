using System.Reflection;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1474: o PWA pareado chamava GET /api/mobile/calculadora/produtos-com-receita
/// so com X-Mobile-Api-Key e recebia 401, porque o controller exigia JWT. As rotas de
/// leitura agora aceitam o device; o tenant vem do aparelho.
/// </summary>
public sealed class MobileCalculadoraDeviceAuthTests
{
    private readonly Guid _empresaDevice = Guid.NewGuid();
    private readonly IProdutoComposicaoRepository _repo = Substitute.For<IProdutoComposicaoRepository>();
    private readonly ICurrentUserAccessor _anonimo = Substitute.For<ICurrentUserAccessor>();

    public MobileCalculadoraDeviceAuthTests()
    {
        _anonimo.IsAuthenticated.Returns(false);
        _anonimo.EmpresaId.Returns(Guid.Empty);
        _repo.BuscarProdutosFinaisAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Produto>());
    }

    private MobileCalculadoraController Controller(MobileDevice? device)
    {
        // Use cases nao sao tocados por produtos-com-receita.
        var c = new MobileCalculadoraController(null!, null!, null!, null!, _repo, _anonimo)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (device is not null)
            c.HttpContext.Items[MobileAuth.HttpContextItemDevice] = device;
        return c;
    }

    private MobileDevice Device() => new() { Id = "dev-1", ApiKeyHash = "h", EmpresaId = _empresaDevice, LojaId = Guid.NewGuid() };

    [Fact]
    public async Task ProdutosComReceita_com_device_pareado_responde_200_no_tenant_do_aparelho()
    {
        var result = await Controller(Device()).ProdutosComReceita(empresaId: null, q: null, limit: 200);

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received(1).BuscarProdutosFinaisAsync(_empresaDevice, null, 200, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProdutosComReceita_com_device_e_empresaId_de_outro_tenant_responde_403()
    {
        var result = await Controller(Device()).ProdutosComReceita(empresaId: Guid.NewGuid(), q: null);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await _repo.DidNotReceiveWithAnyArgs().BuscarProdutosFinaisAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task ProdutosComReceita_sem_device_e_sem_usuario_responde_401()
    {
        var result = await Controller(device: null).ProdutosComReceita(empresaId: null, q: null);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().BuscarProdutosFinaisAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData(nameof(MobileCalculadoraController.ProdutosComReceita))]
    [InlineData(nameof(MobileCalculadoraController.Calcular))]
    [InlineData(nameof(MobileCalculadoraController.CalcularCesta))]
    [InlineData(nameof(MobileCalculadoraController.PreviewCompra))]
    public void Rotas_de_leitura_aceitam_api_key_do_aparelho(string action)
    {
        var m = typeof(MobileCalculadoraController).GetMethod(action)!;
        m.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        m.GetCustomAttribute<MobileApiKeyAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void CriarCompra_continua_exigindo_usuario_autenticado()
    {
        var m = typeof(MobileCalculadoraController).GetMethod(nameof(MobileCalculadoraController.CriarCompra))!;
        m.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
}
