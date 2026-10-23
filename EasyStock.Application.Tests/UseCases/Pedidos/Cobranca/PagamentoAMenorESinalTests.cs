using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1724-7: pagamento aprovado abaixo do valor da cobrança ficava retido no Mercado Pago sem recebimento nem caminho
/// de devolução. Agora entra como recebimento do Mercado Pago (o estorno online o devolve), sem confirmar o pedido.
/// Decisão do Felipe (22/10): o sinal pago pelo link avisa o cliente pelo canal do pedido.
/// </summary>
public class PagamentoAMenorESinalTests
{
    private static ConfirmarPagamentoPedidoInput Aprovado(Guid pedidoId, decimal valor, string pagamento = "pay-9",
        string? preferencia = null) =>
        new(pedidoId, PagamentoExternoId: pagamento, StatusPagamento: "approved", ValorPago: valor,
            MetodoPagamentoExterno: "pix", TipoPagamentoExterno: "bank_transfer", PagoEm: CobrancaPedidoFixture.Agora,
            ReferenciaExterna: preferencia);

    [Fact]
    public async Task Pagamento_a_menor_vira_recebimento_devolvivel_sem_confirmar_o_pedido()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = f.AdicionarOnline(referencia: "pref-25");

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 20m));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.ValorMenor);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento, "pagamento a menor não leva o pedido à fila");
        var recebimento = f.Pedido.Pagamentos.Should().ContainSingle().Subject;
        recebimento.Referencia.Should().Be("pay-9");
        recebimento.Valor.Should().Be(20m);
        // O estorno online acha o recebimento pela cobrança online paga com o mesmo id de pagamento.
        var retida = f.Cobrancas.Should().ContainSingle(c => c.PagamentoExternoId == "pay-9").Subject;
        retida.EhOnline.Should().BeTrue();
        retida.Status.Should().Be(StatusCobrancaPedido.Paga);
        retida.ValorPago.Should().Be(20m);
        retida.ReferenciaExterna.Should().BeNull("a preferência continua sendo da cobrança original");
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Eventos.Should().ContainSingle(e => e.Tipo == "pagamento_divergente").Which.Detalhes.Should().Contain("devolv");
        await f.Publicador.DidNotReceive().PublicarAsync(f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id,
            Arg.Any<PedidoPagoEvent>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notificacao_repetida_do_pagamento_a_menor_nao_duplica()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-25");
        var uc = f.Confirmar();

        await uc.ExecuteAsync(Aprovado(f.Pedido.Id, 20m));
        var repetida = await uc.ExecuteAsync(Aprovado(f.Pedido.Id, 20m));

        repetida.Situacao.Should().Be(SituacaoConfirmacaoPagamento.ValorMenor);
        f.Pedido.Pagamentos.Should().ContainSingle();
        f.Cobrancas.Count(c => c.PagamentoExternoId == "pay-9").Should().Be(1);
        f.Eventos.Count(e => e.Tipo == "pagamento_divergente").Should().Be(1);
    }

    [Fact]
    public async Task Depois_do_pagamento_a_menor_a_proxima_cobranca_cobra_so_o_restante_e_quita()
    {
        var f = new CobrancaPedidoFixture();
        var original = f.AdicionarOnline(referencia: "pref-25");
        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 20m));
        original.Expirar(CobrancaPedidoFixture.Agora);

        var nova = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, Tentativa: 2));
        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 5m, "pay-10", "pref-1"));

        nova.Valor.Should().Be(5m, "os R$ 20,00 retidos já contam");
        r.Confirmado.Should().BeTrue();
        f.Pedido.TotalPago.Should().Be(25m);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
    }

    [Fact]
    public async Task Link_original_pago_por_inteiro_depois_do_a_menor_confirma_e_o_excedente_fica_devolvivel()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-25");
        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 20m));

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 25m, "pay-10", "pref-25"));

        r.Confirmado.Should().BeTrue("o pagamento retido não é o pedido já pago");
        await f.Estorno.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
        f.Pedido.TotalPago.Should().Be(45m);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
    }

    [Fact]
    public async Task Pedido_do_site_com_pagamento_a_menor_nao_e_cancelado_pelo_job()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = f.AdicionarOnline(referencia: "pref-25");
        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 20m));
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);

        var r = await f.ProcessarVencida().ExecuteAsync(new CobrancaPedidoVencida(cobranca.Id, f.EmpresaId, f.Pedido.Id, null));

        r.Should().Be(ResultadoExpiracaoCobranca.AguardandoAtendente, "com dinheiro recebido a equipe decide o acerto");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task Sinal_pago_pelo_link_avisa_o_cliente_uma_vez_com_o_sinal_e_o_restante()
    {
        var f = Encomenda();
        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));
        var uc = f.Confirmar(f.AvisoCliente());

        await uc.ExecuteAsync(Aprovado(f.Pedido.Id, 12.5m));
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id, 12.5m));

        var mensagem = Mensagens(f).Should().ContainSingle().Subject;
        mensagem.Should().Be($"Recebemos o sinal de {12.5m.ToString("C", Cultura.PtBr)}. " +
            $"O restante de {12.5m.ToString("C", Cultura.PtBr)} fica para a entrega.");
    }

    [Fact]
    public async Task Pagamento_que_quita_a_encomenda_nao_manda_aviso_de_sinal()
    {
        var f = Encomenda();
        f.AdicionarOnline(referencia: "pref-total");

        var r = await f.Confirmar(f.AvisoCliente()).ExecuteAsync(Aprovado(f.Pedido.Id, 25m));

        r.Confirmado.Should().BeTrue();
        Mensagens(f).Should().BeEmpty();
    }

    private static CobrancaPedidoFixture Encomenda()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        return f;
    }

    private static List<string?> Mensagens(CobrancaPedidoFixture f) => f.Notificador.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == "EnfileirarEventoAsync"
            && (TipoEventoNotificacao)c.GetArguments()[0]! == TipoEventoNotificacao.ComandaAtualizada)
        .Select(c => JsonSerializer.Deserialize<Dictionary<string, string?>>((string)c.GetArguments()[2]!)!
            .GetValueOrDefault("mensagem"))
        .ToList();
}
