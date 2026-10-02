using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>
/// S53 (#1283): o pedido ganha o número do dia de produção quando vira compromisso (pago ou na fila); o
/// reagendamento para outro dia troca o número de quem já tinha; pedido fora da fila não é numerado.
/// </summary>
public class NumeradorPedidoDoDiaTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc); // 10:00 em Brasília
    private readonly IPrazoPreparoPedidoQueries _prazo = Substitute.For<IPrazoPreparoPedidoQueries>();
    private readonly ISequenciaDiariaPedido _sequencia = Substitute.For<ISequenciaDiariaPedido>();

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private CalculadoraInicioPrevistoPedido Calculadora() =>
        new(_prazo, new NumeradorPedidoDoDia(_sequencia, new RelogioFixo(Agora)));

    private Pedido NovoPedido(DateOnly? dataJanela = null)
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        _prazo.ObterAsync(pedido.EmpresaId, pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(dataJanela, dataJanela is null ? null : new TimeOnly(10, 0), [null], 60, 40));
        return pedido;
    }

    [Fact]
    public async Task CompromissoNumeraPeloDiaDeProducao()
    {
        var pedido = NovoPedido(dataJanela: new DateOnly(2026, 10, 5));
        _sequencia.ProximoAsync(pedido.EmpresaId, new DateOnly(2026, 10, 5), Arg.Any<CancellationToken>()).Returns(7);

        await Calculadora().AplicarCompromissoAsync(pedido);

        pedido.NumeroDoDia.Should().Be(7);
        pedido.DataNumero.Should().Be(new DateOnly(2026, 10, 5), "o dia é o da janela, não o do pagamento");
        pedido.InicioPrevistoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task SemJanelaNemAgendamentoUsaHoje()
    {
        var pedido = NovoPedido();
        _sequencia.ProximoAsync(pedido.EmpresaId, new DateOnly(2026, 10, 2), Arg.Any<CancellationToken>()).Returns(1);

        await Calculadora().AplicarCompromissoAsync(pedido);

        pedido.NumeroDoDia.Should().Be(1);
        pedido.DataNumero.Should().Be(new DateOnly(2026, 10, 2));
    }

    [Fact]
    public async Task MesmoDiaNaoGastaOutroNumero()
    {
        var pedido = NovoPedido();
        _sequencia.ProximoAsync(default, default, default).ReturnsForAnyArgs(4, 5);

        await Calculadora().AplicarCompromissoAsync(pedido);
        await Calculadora().AplicarCompromissoAsync(pedido);

        pedido.NumeroDoDia.Should().Be(4);
        await _sequencia.ReceivedWithAnyArgs(1).ProximoAsync(default, default, default);
    }

    [Fact]
    public async Task ReagendarParaOutroDiaTrocaONumero()
    {
        var pedido = NovoPedido(dataJanela: new DateOnly(2026, 10, 5));
        _sequencia.ProximoAsync(default, default, default).ReturnsForAnyArgs(7, 2);
        await Calculadora().AplicarCompromissoAsync(pedido);

        _prazo.ObterAsync(pedido.EmpresaId, pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(new DateOnly(2026, 10, 6), new TimeOnly(10, 0), [null], 60, 40));
        await Calculadora().RecalcularAsync(pedido);

        pedido.NumeroDoDia.Should().Be(2);
        pedido.DataNumero.Should().Be(new DateOnly(2026, 10, 6));
    }

    [Fact]
    public async Task ReagendarPedidoForaDaFilaNaoNumera()
    {
        var pedido = NovoPedido(dataJanela: new DateOnly(2026, 10, 5));

        await Calculadora().RecalcularAsync(pedido);

        pedido.NumeroDoDia.Should().BeNull();
        await _sequencia.DidNotReceiveWithAnyArgs().ProximoAsync(default, default, default);
    }

    [Fact]
    public async Task EntradaNaFilaNumeraSoQuemEstaAguardando()
    {
        var foraDaFila = NovoPedido();
        foraDaFila.Status = StatusPedidoMapper.Format(StatusPedido.AguardandoPagamento);
        await Calculadora().AplicarNaFilaAsync(foraDaFila);
        foraDaFila.NumeroDoDia.Should().BeNull("aguardando pagamento ainda não é compromisso");

        var naFila = NovoPedido();
        naFila.StatusEnum.Should().Be(StatusPedido.Aguardando);
        _sequencia.ProximoAsync(default, default, default).ReturnsForAnyArgs(9);
        await Calculadora().AplicarNaFilaAsync(naFila);
        naFila.NumeroDoDia.Should().Be(9);
    }
}
