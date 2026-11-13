using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>#2211: todo pedido que ganha canhoto avisa a equipe inscrita, com o canhoto em imagem.</summary>
public class QuitacaoPedidoAvisoEquipeTests
{
    [Fact]
    public async Task CanhotoNovoEnfileiraOAvisoComAChaveDoPedido()
    {
        var f = new CobrancaPedidoFixture();
        var quitacao = QuitacaoPedidoTeste.Criar(f.PedidoRepo, impressoes: f.ImpressaoRepo, notificador: f.Notificador);

        (await quitacao.PrepararCanhotoAsync(f.Pedido, CobrancaPedidoFixture.Agora, default)).Should().NotBeNull();

        await f.Notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, f.EmpresaId,
            Arg.Is<string>(p => p.Contains(f.Pedido.Id.ToString())), f.Pedido.Id, Arg.Any<CancellationToken>(),
            $"aviso-equipe-canhoto:{f.Pedido.Id:N}");
    }

    [Fact]
    public async Task CanhotoQueJaExisteNaoAvisaDeNovo()
    {
        var f = new CobrancaPedidoFixture();
        f.ImpressaoRepo.ExisteCanhotoAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(true);
        var quitacao = QuitacaoPedidoTeste.Criar(f.PedidoRepo, impressoes: f.ImpressaoRepo, notificador: f.Notificador);

        (await quitacao.PrepararCanhotoAsync(f.Pedido, CobrancaPedidoFixture.Agora, default)).Should().BeNull();

        await f.Notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }
}
