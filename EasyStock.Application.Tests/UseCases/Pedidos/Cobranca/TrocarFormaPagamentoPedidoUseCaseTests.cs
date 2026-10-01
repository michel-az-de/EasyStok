using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11, feedback da operadora (26/09/2026): trocar a forma de pagamento sem esperar o link expirar. A
/// cobrança pendente vira <c>Cancelada</c> e nasce outra na forma pedida; pedido pago recusa; pagamento
/// aprovado de uma preferência já cancelada confirma mesmo assim (dinheiro recebido vence).
/// </summary>
public class TrocarFormaPagamentoPedidoUseCaseTests
{
    private static readonly Guid Usuario = Guid.NewGuid();

    [Fact]
    public async Task NaEntregaParaOnlineGeraNovoLink()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var naEntrega = f.AdicionarNaEntrega();

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        naEntrega.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        var nova = f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente).Subject;
        nova.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
        nova.Tentativa.Should().Be(1, "a troca não consome a reemissão do job");
        r.Cobranca.LinkPagamento.Should().Be("https://mp.test/pref-1");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "trocar a forma não cancela o pedido");
        f.Eventos.Should().Contain(e => e.Tipo == "forma_pagamento_trocada" && e.UsuarioId == Usuario);
    }

    [Fact]
    public async Task OnlineParaNaEntrega_ColocaPedidoNaFilaSemLink()
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline();

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        online.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        r.Cobranca.LinkPagamento.Should().BeNull();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "pagamento na entrega libera o pedido para a fila");
        f.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlineParaNaEntrega_GravaInicioPrevisto()
    {
        // #1230: o pedido que vai pagar na entrega entra na fila; o início previsto vale como no Mercado Pago.
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var entrega = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);
        f.Pedido.AgendadoParaEm = entrega;
        f.PrazoQueries.ObterAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(null, null, [null], TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40));

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        f.Pedido.InicioPrevistoEm.Should().Be(entrega.AddMinutes(-100));
    }

    [Fact]
    public async Task PedidoPagoRecusa()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarOnline().MarcarPaga("pay-1", 25m, "pix", CobrancaPedidoFixture.Agora);

        var act = () => f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("pedido_ja_pago");
    }

    [Fact]
    public async Task ApprovedDaCanceladaConfirma()
    {
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline(referencia: "pref-antiga");
        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));
        var naEntrega = f.Cobrancas.Single(c => c.Provedor == CobrancaPedido.ProvedorNaEntrega);

        var r = await f.Confirmar().ExecuteAsync(new ConfirmarPagamentoPedidoInput(
            f.Pedido.Id, "pay-7", "approved", 25m, "visa", "credit_card", CobrancaPedidoFixture.Agora,
            ReferenciaExterna: "pref-antiga"));

        r.Confirmado.Should().BeTrue();
        antiga.Status.Should().Be(StatusCobrancaPedido.Paga);
        antiga.MetodoPagamento.Should().Be("credito");
        naEntrega.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        naEntrega.Motivo.Should().Contain("pago_por_outra_cobranca");
        f.Pedido.Pagamentos.Should().ContainSingle(p => p.Referencia == "pay-7");
    }

    /// <summary>S32: o link antigo para de aceitar pagamento quando a forma muda.</summary>
    [Fact]
    public async Task TrocaExpiraAPreferenciaAnteriorNoMercadoPago()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-antiga");

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-antiga", CobrancaPedidoFixture.Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaAoExpirarPreferencia_NaoDesfazATroca()
    {
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline(referencia: "pref-antiga");
        f.MpClient.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("mp fora"));

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        antiga.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
    }

    [Fact]
    public async Task TrocaSemCobrancaOnlineAnterior_NaoChamaOMercadoPago()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarNaEntrega();

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }
}
