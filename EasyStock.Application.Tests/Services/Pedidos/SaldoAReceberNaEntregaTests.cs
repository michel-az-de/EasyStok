using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>
/// #1776 (item 19): sinal de R$ 25 num pedido de R$ 50 aparecia "Pago" e "Entregue" passava com a
/// cobrança pendente. O que falta receber sai do saldo financeiro (alocado), não de "tem pagamento".
/// </summary>
public class SaldoAReceberNaEntregaTests
{
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IEstornoOnlineRepository _estornos = Substitute.For<IEstornoOnlineRepository>();
    private readonly ICreditoClienteComandaRepository _creditos = Substitute.For<ICreditoClienteComandaRepository>();
    private readonly Pedido _pedido;

    public SaldoAReceberNaEntregaTests()
    {
        _pedido = Pedido.Criar(Guid.NewGuid(), null, Guid.NewGuid(), "site");
        _pedido.Total = Dinheiro.FromDecimal(50m);
        _estornos.ListarAsync(default, default, default).ReturnsForAnyArgs([]);
        _estornos.RecebimentosAsync(default, default, default).ReturnsForAnyArgs([]);
        _pedidos.ListarEstornosManuaisAsync(default, default, default).ReturnsForAnyArgs([]);
        _creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 0m));
    }

    private SaldoFinanceiroPedido Saldos() => new(_pedidos, _estornos, _creditos);

    private void Pagar(decimal valor) =>
        _pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = _pedido.Id, Metodo = "pix", Valor = valor });

    [Fact]
    public async Task SoOSinal_FaltaReceberAMetade()
    {
        Pagar(25m);

        (await Saldos().AReceberAsync(_pedido, CancellationToken.None)).Should().Be(25m);
    }

    [Fact]
    public async Task Quitado_NadaAReceber()
    {
        Pagar(25m);
        Pagar(25m);

        (await Saldos().AReceberAsync(_pedido, CancellationToken.None)).Should().Be(0m);
    }

    [Fact]
    public async Task CupomCobreOResto_NadaAReceber()
    {
        Pagar(25m);
        _creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 25m));

        (await Saldos().AReceberAsync(_pedido, CancellationToken.None)).Should().Be(0m);
    }

    [Fact]
    public async Task EntregarComSaldo_SemConfirmar_Recusa()
    {
        Pagar(25m);

        var act = () => Saldos().ExigirQuitadoParaEntregarAsync(_pedido, saldoConfirmado: false, CancellationToken.None);

        var erro = (await act.Should().ThrowAsync<SaldoAReceberNaEntregaException>()).Which;
        erro.Valor.Should().Be(25m);
        erro.Message.Should().Contain("R$ 25,00");
    }

    [Fact]
    public async Task EntregarComSaldo_Confirmado_Passa()
    {
        Pagar(25m);

        await Saldos().Invoking(s => s.ExigirQuitadoParaEntregarAsync(_pedido, saldoConfirmado: true, CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
