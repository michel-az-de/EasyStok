using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Operacao;

/// <summary>
/// S21: regra do <c>PedidoAtrasoJob</c> (a cada 60 s), que vive em <c>NotificarAtrasoPedidoUseCase</c>.
/// Pedido aguardando com o início previsto vencido publica <c>pedido.atrasado</c> uma única vez.
/// </summary>
public class PedidoAtrasoJobTests
{
    private static PedidoAtrasoCandidato Item(CobrancaPedidoFixture f) => new(f.Pedido.Id, f.EmpresaId);

    [Fact]
    public async Task NotificaUmaVez()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.Pedido.DefinirInicioPrevisto(CobrancaPedidoFixture.Agora.AddMinutes(-5));
        var numero = f.Pedido.Id.ToString("N")[..8].ToUpperInvariant();
        var uc = f.NotificarAtraso();

        var primeira = await uc.ExecuteAsync(Item(f));
        var segunda = await uc.ExecuteAsync(Item(f));

        primeira.Should().BeTrue();
        segunda.Should().BeFalse("o atraso do mesmo início previsto não se repete");
        f.Pedido.AtrasoNotificadoEm.Should().Be(CobrancaPedidoFixture.Agora);
        f.Tenant.Received().SetCurrentTenant(f.EmpresaId);
        await f.OperacaoEventos.Received(1).PublicarAsync(
            EventosOperacao.PedidoAtrasado, f.EmpresaId,
            Arg.Is<PedidoAtrasadoOperacao>(e =>
                e.PedidoId == f.Pedido.Id &&
                e.Numero == numero &&
                e.InicioPrevistoEm == CobrancaPedidoFixture.Agora.AddMinutes(-5)),
            Arg.Any<CancellationToken>());
        await f.Uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PreparoComecadoOuNoPrazo_NaoNotifica()
    {
        var noPrazo = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        noPrazo.Pedido.DefinirInicioPrevisto(CobrancaPedidoFixture.Agora.AddMinutes(5));
        var preparando = new CobrancaPedidoFixture(StatusPedidoMapper.Preparando);
        preparando.Pedido.DefinirInicioPrevisto(CobrancaPedidoFixture.Agora.AddMinutes(-5));

        (await noPrazo.NotificarAtraso().ExecuteAsync(Item(noPrazo))).Should().BeFalse();
        (await preparando.NotificarAtraso().ExecuteAsync(Item(preparando))).Should().BeFalse();

        noPrazo.OperacaoEventos.ReceivedCalls().Should().BeEmpty();
        preparando.OperacaoEventos.ReceivedCalls().Should().BeEmpty();
        preparando.Pedido.AtrasoNotificadoEm.Should().BeNull();
    }
}
