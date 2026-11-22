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
            Arg.Is<string>(p => p.Contains(f.Pedido.Id.ToString()) && !p.Contains("aReceber")), f.Pedido.Id,
            Arg.Any<CancellationToken>(),
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

    [Fact]
    public async Task QuitacaoDireta_AvisaUmaVezSo()
    {
        // #3168: o aviso "pago" posterior não pode dobrar o aviso do pedido que já nasce quitado.
        var f = new CobrancaPedidoFixture(EasyStock.Domain.Sales.StatusPedidoMapper.Aguardando);

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 25m));

        await f.Notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, f.EmpresaId,
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
        await f.Notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, f.EmpresaId,
            Arg.Is<string>(p => !p.Contains("aReceber")), f.Pedido.Id, Arg.Any<CancellationToken>(),
            $"aviso-equipe-canhoto:{f.Pedido.Id:N}");
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(10, 0)] // #3002: sem quitar, nada de "pago"
    public async Task QuitacaoPorCredito_ComCanhotoExistente_AvisaPagoSoQuandoQuita(int pago, int avisos)
    {
        var f = new CobrancaPedidoFixture(EasyStock.Domain.Sales.StatusPedidoMapper.Aguardando);
        f.AdicionarPagamento("pix-direto", pago);
        f.ImpressaoRepo.ExisteCanhotoAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(true);
        var quitacao = QuitacaoPedidoTeste.Criar(f.PedidoRepo, impressoes: f.ImpressaoRepo, saldos: f.Saldos(), notificador: f.Notificador);

        await quitacao.PrepararCreditoAsync(f.Pedido, Guid.NewGuid(), CobrancaPedidoFixture.Agora, default);

        await f.Notificador.Received(avisos).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, f.EmpresaId,
            Arg.Is<string>(p => !p.Contains("aReceber") && p.Contains($"aviso-equipe-canhoto-pago:{f.Pedido.Id:N}")), f.Pedido.Id,
            Arg.Any<CancellationToken>(), $"aviso-equipe-canhoto-pago:{f.Pedido.Id:N}");
        await f.Notificador.Received(avisos).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
        await f.ImpressaoRepo.DidNotReceive().AddAsync(Arg.Any<EasyStock.Domain.Entities.Operacao.ImpressaoPendente>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuitacaoRepetida_ComCanhotoExistente_NaoAvisa()
    {
        // #3168: o marco da quitação segura o segundo aviso "pago".
        var f = new CobrancaPedidoFixture(EasyStock.Domain.Sales.StatusPedidoMapper.Aguardando);
        f.ImpressaoRepo.ExisteCanhotoAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(true);
        f.PedidoRepo.ExisteEventoAsync(f.EmpresaId, f.Pedido.Id, EasyStock.Application.Services.Pedidos.QuitacaoPedido.Marco,
            Arg.Any<CancellationToken>()).Returns(true);

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 25m));

        await f.Notificador.DidNotReceive().EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto,
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }
}
