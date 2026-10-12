using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public partial class AtendimentoConversasControllerTests
{
    private EnviarModeloConversaUseCase EnvioModelo(ICatalogoModelosAtendimento catalogo) => new(
        _repositorio, Substitute.For<IClienteRepository>(), catalogo,
        new PoliticaEnvioCliente(Substitute.For<IConsentimentoContatoRepository>()),
        new ResolvedorCanal([_canal]), new FakeUnitOfWork(), TimeProvider.System);

    [Fact]
    public async Task Modelos_PermissaoAntesDaConsultaOuDoEnvio()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        (await _controller.ListarModelos(new(catalogo), cache)).Should().BeOfType<ForbidResult>();
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.NewGuid(), "retomar", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<ForbidResult>();
        catalogo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Modelos_CacheCurtoPorEmpresa_AtualizacaoExplicita()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var listar = new ListarModelosAtendimentoUseCase(catalogo);
        await _controller.ListarModelos(listar, cache);
        await _controller.ListarModelos(listar, cache);
        await catalogo.Received(1).ListarAsync(_empresaId, Arg.Any<CancellationToken>());
        await _controller.ListarModelos(listar, cache, atualizar: true);
        await catalogo.Received(2).ListarAsync(_empresaId, Arg.Any<CancellationToken>());
        var outra = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(outra);
        await _controller.ListarModelos(listar, cache);
        await catalogo.Received(1).ListarAsync(outra, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modelos_FalhaNoCatalogo502_Inexistente404_InputInvalido400()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<ModeloAtendimento>>>(_ => throw new CatalogoModelosException("Falha"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        (await _controller.ListarModelos(new(catalogo), cache)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(502);
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.NewGuid(), "modelo", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<NotFoundObjectResult>();
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.Empty, "modelo", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<BadRequestObjectResult>();
    }
}
