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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Viagem_usa_endereco_do_pedido_com_fallback_apenas_para_legado(bool legado)
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.ClienteId = Guid.NewGuid();
        pedido.EnderecoEntrega = legado ? null : "Rua da encomenda, 42";
        _pedidos.ListarPorIdsAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([pedido]);
        _clientes.ListarPorIdsAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new Cliente { Id = pedido.ClienteId.Value, Endereco = "Rua do cadastro, 99" }]);
        var viagem = Viagem.Criar(_empresaId, Agora);
        viagem.IncluirParada(pedido.Id, false);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        var dto = await new ObterViagemUseCase(_viagens, _pedidos, _clientes).ExecuteAsync(_empresaId, viagem.Id);
        dto.Paradas.Single().Endereco.Should().Be(legado ? "Rua do cadastro, 99" : "Rua da encomenda, 42");
        dto.RotaUrl.Should().Contain(Uri.EscapeDataString(legado ? "Rua do cadastro, 99" : "Rua da encomenda, 42"));
    }

    // #2007 (itens 1 e 3): a parada leva as observações (troco do dinheiro na entrega) e o número do dia,
    // para Entregas não depender de o pedido estar na lista do dia que a tela mostra.
    [Fact]
    public async Task Parada_traz_observacoes_e_numero_do_dia_do_pedido()
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.Observacoes = "Pagamento na entrega: dinheiro, troco para R$ 100,00.";
        pedido.DefinirNumeroDoDia(new DateOnly(2026, 10, 24), 7);
        _pedidos.ListarPorIdsAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([pedido]);
        var viagem = Viagem.Criar(_empresaId, Agora);
        viagem.IncluirParada(pedido.Id, false);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var parada = (await new ObterViagemUseCase(_viagens, _pedidos, _clientes).ExecuteAsync(_empresaId, viagem.Id)).Paradas.Single();

        parada.Observacoes.Should().Be("Pagamento na entrega: dinheiro, troco para R$ 100,00.");
        parada.NumeroDoDia.Should().Be(7);
    }

    // #1564: a lista monta todas as paradas com uma consulta de pedidos e uma de clientes, e não uma por parada.
    [Fact]
    public async Task Lista_carrega_pedidos_e_clientes_em_lote()
    {
        var fulana = Cliente.Criar(_empresaId, "Fulana");
        fulana.Endereco = "Rua do cadastro, 99";
        var comEndereco = Pedido.Criar(_empresaId, fulana);
        comEndereco.EnderecoEntrega = "Rua da encomenda, 42";
        var legado = Pedido.Criar(_empresaId, fulana);
        var semCliente = Pedido.Criar(_empresaId);
        semCliente.ClienteNome = "Balcão";
        var sumido = Guid.NewGuid();
        var primeira = Viagem.Criar(_empresaId, Agora);
        primeira.IncluirParada(comEndereco.Id, false);
        primeira.IncluirParada(legado.Id, false);
        var segunda = Viagem.Criar(_empresaId, Agora);
        segunda.IncluirParada(semCliente.Id, false);
        segunda.IncluirParada(sumido, false);
        var vazia = Viagem.Criar(_empresaId, Agora);
        _viagens.ListarAsync(_empresaId, null, ListarViagensUseCase.LimitePadrao, Arg.Any<CancellationToken>())
            .Returns([primeira, segunda, vazia]);
        IReadOnlyCollection<Guid>? pedidosPedidos = null, clientesPedidos = null;
        _pedidos.ListarPorIdsAsync(_empresaId, Arg.Do<IReadOnlyCollection<Guid>>(ids => pedidosPedidos = ids), Arg.Any<CancellationToken>())
            .Returns([comEndereco, legado, semCliente]);
        _clientes.ListarPorIdsAsync(_empresaId, Arg.Do<IReadOnlyCollection<Guid>>(ids => clientesPedidos = ids), Arg.Any<CancellationToken>())
            .Returns([fulana]);

        var lista = await new ListarViagensUseCase(_viagens, new ObterViagemUseCase(_viagens, _pedidos, _clientes))
            .ExecuteAsync(_empresaId, null);

        lista.Select(v => v.Id).Should().Equal(primeira.Id, segunda.Id, vazia.Id);
        lista[0].Paradas.Select(p => (p.PedidoId, p.ClienteNome, p.Endereco)).Should().Equal(
            (comEndereco.Id, "Fulana", "Rua da encomenda, 42"), (legado.Id, "Fulana", "Rua do cadastro, 99"));
        lista[1].Paradas.Select(p => (p.PedidoId, p.ClienteNome, p.Endereco)).Should().Equal(
            (semCliente.Id, "Balcão", (string?)null), (sumido, (string?)null, (string?)null));
        lista[2].Paradas.Should().BeEmpty();
        pedidosPedidos.Should().BeEquivalentTo([comEndereco.Id, legado.Id, semCliente.Id, sumido]);
        clientesPedidos.Should().Equal(fulana.Id);
        await _pedidos.Received(1).ListarPorIdsAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _clientes.Received(1).ListarPorIdsAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _pedidos.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await _clientes.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Viagem_sem_parada_nao_consulta_pedido_nem_cliente()
    {
        var viagem = Viagem.Criar(_empresaId, Agora);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        (await new ObterViagemUseCase(_viagens, _pedidos, _clientes).ExecuteAsync(_empresaId, viagem.Id)).Paradas.Should().BeEmpty();

        _pedidos.ReceivedCalls().Should().BeEmpty();
        _clientes.ReceivedCalls().Should().BeEmpty();
    }

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
