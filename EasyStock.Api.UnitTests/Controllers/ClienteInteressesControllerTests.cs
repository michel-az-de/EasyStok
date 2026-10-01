using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Domain.Entities.Campanhas;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Interesses do cliente no console (S31, #1228): a dona fecha o interesse e lista os do cliente.
/// Interesse de outra empresa ou de outro cliente devolve 404; <c>EmpresaId</c> vem do token.
/// </summary>
public class ClienteInteressesControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IInteresseItemRepository _interesses = Substitute.For<IInteresseItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ClienteInteressesController _controller;

    public ClienteInteressesControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        var relogio = TimeProvider.System;
        _controller = new ClienteInteressesController(
            new RegistrarInteresseItemUseCase(Substitute.For<IClienteRepository>(), Substitute.For<IStorefrontRepository>(),
                Substitute.For<ICardapioItemRepository>(), _interesses, _uow, relogio),
            new MarcarInteresseAtendidoUseCase(_interesses, _uow, relogio),
            new ListarInteressesDoClienteUseCase(_interesses),
            _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private InteresseItem Interesse(Guid? clienteId = null) =>
        InteresseItem.Registrar(_empresaId, clienteId ?? _clienteId, Guid.NewGuid(), "Bolo de Cenoura", OrigemInteresse.Dona, DateTime.UtcNow);

    [Fact]
    public async Task AtendidoDevolve200EGrava()
    {
        var interesse = Interesse();
        _interesses.GetByIdAsync(_empresaId, interesse.Id, Arg.Any<CancellationToken>()).Returns(interesse);

        (await _controller.MarcarAtendido(_clienteId, interesse.Id, CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();

        interesse.AtendidoEm.Should().NotBeNull();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AtendidoDeOutraEmpresaDevolve404()
    {
        (await _controller.MarcarAtendido(_clienteId, Guid.NewGuid(), CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task AtendidoDeOutroClienteDevolve404()
    {
        var deOutro = Interesse(clienteId: Guid.NewGuid());
        _interesses.GetByIdAsync(_empresaId, deOutro.Id, Arg.Any<CancellationToken>()).Returns(deOutro);

        (await _controller.MarcarAtendido(_clienteId, deOutro.Id, CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
        deOutro.AtendidoEm.Should().BeNull();
    }

    [Fact]
    public async Task ListarDevolve200ComOsDaEmpresaDoToken()
    {
        _interesses.ListarDoClienteAsync(_empresaId, _clienteId, Arg.Any<CancellationToken>()).Returns([Interesse()]);

        (await _controller.Listar(_clienteId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();

        await _interesses.Received(1).ListarDoClienteAsync(_empresaId, _clienteId, Arg.Any<CancellationToken>());
    }
}
