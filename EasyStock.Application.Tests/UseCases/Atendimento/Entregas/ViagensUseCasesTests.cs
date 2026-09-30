using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

/// <summary>S44: montar a viagem, bloqueio de cliente, retrato e rota no Maps.</summary>
public class ViagensUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IViagemRepository _viagens = Substitute.For<IViagemRepository>();
    private readonly IEntregadorRepository _entregadores = Substitute.For<IEntregadorRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    [Fact]
    public async Task ClienteBloqueadoNaoEntra()
    {
        var cliente = Cliente.Criar(_empresaId, "Fulana");
        cliente.Bloquear("calote", Agora);
        var pedido = Pedido.Criar(_empresaId, cliente);
        pedido.Status = StatusPedidoMapper.Pronto;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        var viagem = Viagem.Criar(_empresaId, Agora);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var act = () => new IncluirParadaViagemUseCase(_viagens, _pedidos, _clientes, _uow)
            .ExecuteAsync(_empresaId, viagem.Id, pedido.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*bloqueado*");
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task PedidoEmOutraViagemAtivaNaoEntra()
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        var viagem = Viagem.Criar(_empresaId, Agora);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        _viagens.PedidoEmViagemAtivaAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new IncluirParadaViagemUseCase(_viagens, _pedidos, _clientes, _uow)
            .ExecuteAsync(_empresaId, viagem.Id, pedido.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task IncluiPedidoProntoEGrava()
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        var viagem = Viagem.Criar(_empresaId, Agora);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        await new IncluirParadaViagemUseCase(_viagens, _pedidos, _clientes, _uow).ExecuteAsync(_empresaId, viagem.Id, pedido.Id);

        viagem.Paradas.Should().ContainSingle(p => p.PedidoId == pedido.Id);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task EditarPlacaDepoisDaSaidaNaoMudaODespacho()
    {
        var entregador = Entregador.Criar(_empresaId, "José", TipoEntregador.Plataforma, EmpresaEntregador.NoveNove, null, "Moto", "ABC1D23", Agora);
        _entregadores.ObterAsync(_empresaId, entregador.Id, Arg.Any<CancellationToken>()).Returns(entregador);
        var viagem = Viagem.Criar(_empresaId, Agora);
        viagem.DefinirEntregador(entregador);
        var pedido = Pedido.Criar(_empresaId);
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        viagem.IncluirParada(pedido.Id, false);
        viagem.Sair(entregador, Agora);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        await new AtualizarEntregadorUseCase(_entregadores, _uow, _relogio).ExecuteAsync(new AtualizarEntregadorCommand(
            _empresaId, entregador.Id, "José", TipoEntregador.Plataforma, EmpresaEntregador.NoveNove, null, "Moto", "ZZZ9Z99"));
        var despacho = await new ObterViagemUseCase(_viagens, _pedidos, _clientes).ExecuteAsync(_empresaId, viagem.Id);

        entregador.Placa.Should().Be("ZZZ9Z99");
        despacho.Paradas.Single().Placa.Should().Be("ABC1D23");
        despacho.Paradas.Single().NumeroPedido.Should().Be(pedido.Id.ToString("N")[..8].ToUpperInvariant());
    }

    [Fact]
    public void RotaNoMapsMontaUrlComOsEnderecosSemChave()
    {
        var url = RotaMaps.Montar(new[] { "Rua A, 10 - Centro", null, "Av. B, 200" });

        url.Should().Be("https://www.google.com/maps/dir/Rua%20A%2C%2010%20-%20Centro/Av.%20B%2C%20200");
        RotaMaps.Montar(Array.Empty<string?>()).Should().BeNull();
    }
}
