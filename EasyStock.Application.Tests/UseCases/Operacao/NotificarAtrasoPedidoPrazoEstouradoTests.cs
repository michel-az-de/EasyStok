using EasyStock.Application.Ports.Output.Persistence.Storefront;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Operacao;

/// <summary>N11: o pedido atrasado enfileira o <c>PrazoEstourado</c> no mesmo commit da marca <c>AtrasoNotificadoEm</c>.</summary>
public class NotificarAtrasoPedidoPrazoEstouradoTests
{
    private static PedidoAtrasoCandidato Item(CobrancaPedidoFixture f) => new(f.Pedido.Id, f.EmpresaId);

    private static CobrancaPedidoFixture Atrasado()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.Pedido.DefinirInicioPrevisto(CobrancaPedidoFixture.Agora.AddMinutes(-35));
        return f;
    }

    [Fact]
    public async Task EventoSaiNoMesmoCommitDaMarca()
    {
        var f = Atrasado();
        await f.NotificarAtraso().ExecuteAsync(Item(f));

        Received.InOrder(() =>
        {
            f.Notificador.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, f.EmpresaId, Arg.Any<string>(),
                f.Pedido.Id, Arg.Any<CancellationToken>(), $"prazo:pedido_atrasado:{f.Pedido.Id:N}");
            f.Uow.CommitAsync();
        });
        f.Pedido.AtrasoNotificadoEm.Should().NotBeNull();
        var payload = (string)f.Notificador.ReceivedCalls().Single().GetArguments()[2]!;
        var json = JsonDocument.Parse(payload).RootElement;
        json.GetProperty("referencia").GetString().Should().Be(f.Pedido.Id.ToString("N")[..8].ToUpperInvariant());
        json.GetProperty("atraso_texto").GetString().Should().Be("35 minutos");
        json.EnumerateObject().Select(p => p.Name).Should().NotContain(["cliente", "nome", "telefone", "email"]);
    }

    [Fact]
    public async Task PedidoJaNotificadoNaoEnfileira()
    {
        var f = Atrasado();
        var uc = f.NotificarAtraso();
        await uc.ExecuteAsync(Item(f));
        f.Notificador.ClearReceivedCalls();

        var segunda = await uc.ExecuteAsync(Item(f));

        segunda.Should().BeFalse();
        f.Notificador.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task InterruptorDesligadoMarcaEPublicaSemEnfileirar()
    {
        var f = Atrasado();
        f.Prazos.Habilitado = false;

        var avisou = await f.NotificarAtraso().ExecuteAsync(Item(f));

        avisou.Should().BeTrue();
        f.Notificador.ReceivedCalls().Should().BeEmpty();
        await f.OperacaoEventos.Received(1).PublicarAsync(
            EventosOperacao.PedidoAtrasado, f.EmpresaId, Arg.Any<PedidoAtrasadoOperacao>(), Arg.Any<CancellationToken>());
    }
}
