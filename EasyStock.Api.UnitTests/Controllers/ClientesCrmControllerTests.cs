using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>S24: códigos HTTP do CRM leve (tag duplicada 409, nota com pedido de outro cliente 400).</summary>
public class ClientesCrmControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Cliente _cliente;
    private readonly ClientesCrmController _controller;

    public ClientesCrmControllerTests()
    {
        // Nível padrão do substituto seria SuperAdmin (0), que exige empresaId na query.
        _currentUser.Nivel.Returns(EasyStock.Domain.Enums.NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _cliente = Cliente.Criar(_empresaId, "Maria");
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _crm.ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(_cliente);

        var relogio = TimeProvider.System;
        _controller = new ClientesCrmController(
            new ListarTagsClienteUseCase(_crm),
            new AdicionarTagClienteUseCase(_crm, _uow, relogio),
            new RemoverTagClienteUseCase(_crm, _uow),
            new ListarNotasClienteUseCase(_clientes, _crm),
            new AdicionarNotaClienteUseCase(_clientes, _crm, _uow, relogio),
            new DefinirBloqueioClienteUseCase(_clientes, _uow, relogio),
            new DefinirPreferenciasClienteUseCase(_clientes, _uow, relogio),
            _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task TagDuplicadaDevolve409()
    {
        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("Vegano"), null, CancellationToken.None))
            .Should().BeOfType<CreatedResult>();

        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("vegano"), null, CancellationToken.None))
            .Should().BeOfType<ConflictObjectResult>();

        _cliente.Tags.Should().ContainSingle();
    }

    [Fact]
    public async Task TagInvalidaDevolve400()
    {
        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("!!!"), null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
        await _crm.Received(1).ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotaComPedidoDeOutroClienteDevolve400()
    {
        var pedidoDeOutro = Guid.NewGuid();
        _crm.PedidoEhDoClienteAsync(_empresaId, pedidoDeOutro, _cliente.Id, Arg.Any<CancellationToken>()).Returns(false);

        var resultado = await _controller.AdicionarNota(
            _cliente.Id, new AdicionarNotaBody("chegou frio", pedidoDeOutro), null, CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _crm.Received(1).PedidoEhDoClienteAsync(_empresaId, pedidoDeOutro, _cliente.Id, Arg.Any<CancellationToken>());
        await _crm.DidNotReceiveWithAnyArgs().AdicionarNotaAsync(default!, default);
    }

    [Fact]
    public async Task ClienteDeOutraEmpresaDevolve404()
    {
        (await _controller.Bloquear(Guid.NewGuid(), new BloquearBody("golpe"), null, CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task BloquearDevolveEstado()
    {
        var resultado = await _controller.Bloquear(_cliente.Id, new BloquearBody("golpe"), null, CancellationToken.None);

        resultado.Should().BeOfType<OkObjectResult>();
        _cliente.Bloqueado.Should().BeTrue();
        _cliente.MotivoBloqueio.Should().Be("golpe");
    }
}
