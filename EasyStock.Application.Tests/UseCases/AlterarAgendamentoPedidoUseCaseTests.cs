using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AlterarAgendamentoPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class AlterarAgendamentoPedidoUseCaseTests
{
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private AlterarAgendamentoPedidoUseCase Sut() => new(_pedidoRepo, _uow,
        Substitute.For<ILogger<AlterarAgendamentoPedidoUseCase>>());

    [Fact]
    public async Task DeveReagendarPedido_QuandoDataFutura()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        var novaData = DateTime.UtcNow.AddHours(3);
        var result = await Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, pedido.Id, novaData));

        result.Should().NotBeNull();
        pedido.AgendadoParaEm.Should().Be(novaData);
        result!.AgendadoParaEm.Should().Be(novaData);
        await _pedidoRepo.Received(1).AddEventoAsync(Arg.Is<PedidoEvento>(e => e.Tipo == "agendamento_alterado"));
        await _pedidoRepo.Received(1).UpdateAsync(pedido);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveRemoverAgendamento_QuandoAgendadoParaEmNull()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.AgendadoParaEm = DateTime.UtcNow.AddHours(5);
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        var result = await Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, pedido.Id, AgendadoParaEm: null));

        result.Should().NotBeNull();
        pedido.AgendadoParaEm.Should().BeNull();
        result!.AgendadoParaEm.Should().BeNull();
        await _pedidoRepo.Received(1).AddEventoAsync(Arg.Is<PedidoEvento>(
            e => e.Tipo == "agendamento_alterado" && e.Detalhes!.Contains("removido")));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoDataNoPassado()
    {
        var empresaId = Guid.NewGuid();
        var dataPassada = DateTime.UtcNow.AddMinutes(-5);

        var act = () => Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, Guid.NewGuid(), dataPassada));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*futuro*");
        await _pedidoRepo.DidNotReceive().UpdateAsync(Arg.Any<Pedido>());
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoPedidoEntregue()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.MudarStatus(Domain.Sales.StatusPedido.Preparando);
        pedido.MudarStatus(Domain.Sales.StatusPedido.Pronto);
        pedido.MudarStatus(Domain.Sales.StatusPedido.Entregue);
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        var act = () => Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, pedido.Id, DateTime.UtcNow.AddHours(2)));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*entregue ou cancelado*");
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoPedidoCancelado()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.Cancelar();
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        var act = () => Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, pedido.Id, DateTime.UtcNow.AddHours(2)));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*entregue ou cancelado*");
    }

    [Fact]
    public async Task DeveRetornarNull_QuandoPedidoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        _pedidoRepo.GetByIdAsync(empresaId, pedidoId).Returns((Pedido?)null);

        var result = await Sut().ExecuteAsync(new AlterarAgendamentoPedidoCommand(
            empresaId, pedidoId, DateTime.UtcNow.AddHours(1)));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }
}
