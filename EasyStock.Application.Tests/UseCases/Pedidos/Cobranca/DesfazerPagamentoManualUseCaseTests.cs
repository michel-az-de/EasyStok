using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11: "marcar como pago" por engano se desfaz com motivo, e só para pagamento registrado à mão.
/// Pagamento do Mercado Pago não se desfaz aqui: é estorno (S27).
/// </summary>
public class DesfazerPagamentoManualUseCaseTests
{
    private static readonly Guid Usuario = Guid.NewGuid();

    [Fact]
    public async Task VoltaAguardandoPagamento()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarOnline();
        f.AdicionarPagamento(referencia: null);

        var r = await f.Desfazer().ExecuteAsync(
            new DesfazerPagamentoManualInput(f.EmpresaId, f.Pedido.Id, "marquei o pedido errado", Usuario, "Operadora"));

        r.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Eventos.Should().Contain(e =>
            e.Tipo == "pagamento_desfeito" && e.UsuarioId == Usuario && e.Detalhes!.Contains("marquei o pedido errado"));
    }

    [Fact]
    public async Task PagamentoMercadoPagoRecusa()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarOnline().MarcarPaga("pay-9", 25m, "pix", CobrancaPedidoFixture.Agora);
        f.AdicionarPagamento(referencia: "pay-9");

        var act = () => f.Desfazer().ExecuteAsync(
            new DesfazerPagamentoManualInput(f.EmpresaId, f.Pedido.Id, "engano", Usuario, "Operadora"));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("use_estorno");
        f.Pedido.Pagamentos.Should().ContainSingle();
    }

    [Fact]
    public async Task SemMotivo_Recusa()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarPagamento(referencia: null);

        var act = () => f.Desfazer().ExecuteAsync(
            new DesfazerPagamentoManualInput(f.EmpresaId, f.Pedido.Id, "  ", Usuario, "Operadora"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task PreparoIniciado_Recusa()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Preparando);
        f.AdicionarPagamento(referencia: null);

        var act = () => f.Desfazer().ExecuteAsync(
            new DesfazerPagamentoManualInput(f.EmpresaId, f.Pedido.Id, "engano", Usuario, "Operadora"));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("preparo_iniciado");
    }

    [Fact]
    public async Task PagamentoNaEntrega_PedidoFicaNaFila()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarNaEntrega();
        f.AdicionarPagamento(referencia: null);

        var r = await f.Desfazer().ExecuteAsync(
            new DesfazerPagamentoManualInput(f.EmpresaId, f.Pedido.Id, "engano", Usuario, "Operadora"));

        r.Status.Should().Be(StatusPedidoMapper.Aguardando, "sem link online pendente o pedido segue na fila e paga na entrega");
        f.Pedido.Pagamentos.Should().BeEmpty();
    }
}
