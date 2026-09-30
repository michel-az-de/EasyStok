using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

/// <summary>S44: "sair para entrega" da viagem transita todos os pedidos e publica o aviso de cada um.</summary>
public class SairParaEntregaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IViagemRepository _viagens = Substitute.For<IViagemRepository>();
    private readonly IEntregadorRepository _entregadores = Substitute.For<IEntregadorRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IPublicadorEventoIntegracao _publicador = Substitute.For<IPublicadorEventoIntegracao>();
    private readonly IOperacaoEventPublisher _operacao = Substitute.For<IOperacaoEventPublisher>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    private SairParaEntregaUseCase Criar() => new(_viagens, _entregadores, _pedidos, _publicador, _operacao, _uow, _relogio);

    private Pedido PedidoPronto()
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        return pedido;
    }

    [Fact]
    public async Task AvisaCadaParada()
    {
        var entregador = Entregador.Criar(_empresaId, "José", TipoEntregador.Motoboy, EmpresaEntregador.Propria, null, "Moto", "ABC1D23", Agora);
        _entregadores.ObterAsync(_empresaId, entregador.Id, Arg.Any<CancellationToken>()).Returns(entregador);
        var viagem = Viagem.Criar(_empresaId, Agora);
        viagem.DefinirEntregador(entregador);
        var pedidos = new[] { PedidoPronto(), PedidoPronto(), PedidoPronto() };
        foreach (var p in pedidos) viagem.IncluirParada(p.Id, false);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var resultado = await Criar().ExecuteAsync(_empresaId, viagem.Id);

        pedidos.Should().AllSatisfy(p => p.Status.Should().Be(StatusPedidoMapper.SaiuParaEntrega));
        foreach (var p in pedidos)
        {
            await _publicador.Received(1).PublicarAsync(
                _empresaId, "pedido.mudou_status", "pedido", p.Id,
                Arg.Is<PedidoMudouStatusEvent>(e => e.StatusNovo == StatusPedidoMapper.SaiuParaEntrega),
                Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        }
        _uow.CommitCount.Should().Be(1);
        resultado.Situacao.Should().Be(SituacaoViagem.EmRota);
        resultado.Paradas.Should().AllSatisfy(p => p.Placa.Should().Be("ABC1D23"));
    }

    [Fact]
    public async Task SemEntregadorRecusaENaoAvisa()
    {
        var viagem = Viagem.Criar(_empresaId, Agora);
        var pedido = PedidoPronto();
        viagem.IncluirParada(pedido.Id, false);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var act = () => Criar().ExecuteAsync(_empresaId, viagem.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*RN-32*");
        pedido.Status.Should().Be(StatusPedidoMapper.Pronto);
        _uow.CommitCount.Should().Be(0);
        await _publicador.DidNotReceiveWithAnyArgs().PublicarAsync<PedidoMudouStatusEvent>(default, default!, default!, default, default!);
    }

    [Fact]
    public async Task PedidoQueNaoPodeSairRecusaSemGravar()
    {
        var entregador = Entregador.Criar(_empresaId, "José", TipoEntregador.Motoboy, EmpresaEntregador.Propria, null, null, null, Agora);
        _entregadores.ObterAsync(_empresaId, entregador.Id, Arg.Any<CancellationToken>()).Returns(entregador);
        var viagem = Viagem.Criar(_empresaId, Agora);
        viagem.DefinirEntregador(entregador);
        var cancelado = Pedido.Criar(_empresaId);
        cancelado.Status = StatusPedidoMapper.Cancelado;
        _pedidos.GetByIdAsync(_empresaId, cancelado.Id).Returns(cancelado);
        viagem.IncluirParada(cancelado.Id, false);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var act = () => Criar().ExecuteAsync(_empresaId, viagem.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }
}
