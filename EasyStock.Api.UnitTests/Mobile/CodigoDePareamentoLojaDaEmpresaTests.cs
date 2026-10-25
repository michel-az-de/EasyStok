using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2033: o código de pareamento conferia só a empresa. Com a loja de outra empresa, o aparelho pareado assinava o
/// SSE com a loja dela e recebia os pedidos prontos dela.
/// </summary>
public sealed class CodigoDePareamentoLojaDaEmpresaTests : IDisposable
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly DevicePairingController _controller;

    public CodigoDePareamentoLojaDaEmpresaTests()
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.EmpresaId.Returns(_empresa);
        usuario.Nivel.Returns(NivelAcesso.Gerente);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"pareamento-{Guid.NewGuid()}").Options, usuario);
        _controller = new DevicePairingController(_db, usuario, new ConfigurationBuilder().Build(),
            NullLogger<DevicePairingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose() => _db.Dispose();

    private Guid Loja(Guid empresa, bool ativa = true)
    {
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Loja", Ativa = ativa };
        _db.Add(loja);
        _db.SaveChanges();
        return loja.Id;
    }

    [Fact]
    public async Task LojaDeOutraEmpresa_Recusa()
    {
        Loja(_empresa); // a empresa tem loja: a conferência é desta loja, não de "alguma loja da empresa"
        var alheia = Loja(Guid.NewGuid());

        var resposta = await _controller.CreatePairCode(new CreatePairCodeRequest(_empresa, alheia, null, "Celular", null), default);

        resposta.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.Set<MobileDevice>().IgnoreQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public async Task LojaDaPropriaEmpresa_GeraOCodigo()
    {
        var propria = Loja(_empresa);

        var resposta = await _controller.CreatePairCode(new CreatePairCodeRequest(_empresa, propria, null, "Celular", null), default);

        resposta.Result.Should().BeOfType<OkObjectResult>();
        _db.Set<MobileDevice>().IgnoreQueryFilters().Should().ContainSingle().Which.LojaId.Should().Be(propria);
    }

    [Fact]
    public async Task LojaDesativada_Recusa()
    {
        var desativada = Loja(_empresa, ativa: false);

        var resposta = await _controller.CreatePairCode(new CreatePairCodeRequest(_empresa, desativada, null, "Celular", null), default);

        resposta.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
