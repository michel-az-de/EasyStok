using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Application.UseCases.Atendimento.Frete;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases;

public sealed class ConsultarViagemClienteUseCaseTests
{
    private readonly Guid empresa = Guid.NewGuid();
    private readonly IClienteRepository clientes = Substitute.For<IClienteRepository>();
    private readonly IPedidoRepository pedidos = Substitute.For<IPedidoRepository>();
    private readonly IViagemRepository viagens = Substitute.For<IViagemRepository>();
    private ConsultarViagemClienteUseCase Criar() => new(clientes, pedidos, viagens, new ObterViagemUseCase(viagens, pedidos, clientes));

    private (Cliente Cliente, Pedido Pedido) Preparar()
    {
        var cliente = Cliente.Criar(empresa, "Cliente da viagem");
        cliente.Endereco = "Rua de teste, 10";
        var pedido = Pedido.Criar(empresa, cliente);
        clientes.GetByIdAsync(empresa, cliente.Id).Returns(cliente);
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(pedido);
        clientes.ListarPorIdsAsync(empresa, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([cliente]);
        pedidos.ListarPorIdsAsync(empresa, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([pedido]);
        return (cliente, pedido);
    }

    [Fact]
    public async Task SemViagemAtiva_NaoInventaRoteiroNemSaida()
    {
        var (cliente, pedido) = Preparar();
        var resultado = await Criar().ExecuteAsync(empresa, cliente.Id, pedido.Id);
        resultado!.PedidoId.Should().Be(pedido.Id);
        resultado.Viagem.Should().BeNull();
        await viagens.DidNotReceive().ObterAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ViagemEmRota_UsaProjecaoExistenteComParadasESaidaReais()
    {
        var (cliente, pedido) = Preparar();
        var viagem = Viagem.Criar(empresa, DateTime.UtcNow);
        viagem.IncluirParada(pedido.Id, false);
        var entregador = Entregador.Criar(empresa, "Entregador de teste", TipoEntregador.Proprio, EmpresaEntregador.Propria, null, null, null, DateTime.UtcNow);
        viagem.DefinirEntregador(entregador);
        viagem.Sair(entregador, DateTime.UtcNow);
        viagens.ObterAtivaDoPedidoAsync(empresa, pedido.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        viagens.ObterAsync(empresa, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);

        var resultado = await Criar().ExecuteAsync(empresa, cliente.Id, pedido.Id);
        resultado!.Viagem!.Id.Should().Be(viagem.Id);
        resultado.Viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        resultado.Viagem.SaiuEm.Should().Be(viagem.SaiuEm);
        resultado.Viagem.Paradas.Should().ContainSingle(p => p.PedidoId == pedido.Id && p.ClienteNome == cliente.Nome);
        resultado.Viagem.RotaUrl.Should().StartWith(RotaMaps.Base);
    }

    [Fact]
    public async Task OutroTenant_NaoConsultaPedidosOuViagens()
    {
        var (cliente, pedido) = Preparar();
        pedidos.ClearReceivedCalls();
        (await Criar().ExecuteAsync(Guid.NewGuid(), cliente.Id, pedido.Id)).Should().BeNull();
        pedidos.ReceivedCalls().Should().BeEmpty();
        viagens.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PedidoDeOutroClienteOuEmpresa_NaoConsultaViagem(bool outraEmpresa)
    {
        var (cliente, pedido) = Preparar();
        var empresaDoPedido = outraEmpresa ? Guid.NewGuid() : empresa;
        var outro = Pedido.Criar(empresaDoPedido, Cliente.Criar(empresaDoPedido, "Outro cliente"));
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(outro);
        (await Criar().ExecuteAsync(empresa, cliente.Id, pedido.Id)).Should().BeNull();
        viagens.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViagemDeOutroTenantOuSemParadaDoPedido_NaoExibeRoteiro(bool outraEmpresa)
    {
        var (cliente, pedido) = Preparar();
        var viagem = Viagem.Criar(outraEmpresa ? Guid.NewGuid() : empresa, DateTime.UtcNow);
        viagem.IncluirParada(outraEmpresa ? pedido.Id : Guid.NewGuid(), false);
        viagens.ObterAtivaDoPedidoAsync(empresa, pedido.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        (await Criar().ExecuteAsync(empresa, cliente.Id, pedido.Id)).Should().BeNull();
        await viagens.DidNotReceive().ObterAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
