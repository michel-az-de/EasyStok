using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #2813: o pedido entregue do histórico entra quitado pela marca <c>QuitadoHistoricoEm</c>, sem pagamento nem movimento
/// de caixa. Quem cria pagamento ou conta o que falta receber trata o pedido marcado como quitado e não cria nada.
/// </summary>
public class PedidoQuitadoHistoricoTests
{
    private static CobrancaPedidoFixture EntregueQuitadoNoHistorico()
    {
        var f = new CobrancaPedidoFixture("entregue");
        f.Pedido.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);
        return f;
    }

    [Fact]
    public void Marca_so_vale_para_pedido_entregue_e_sem_pagamento()
    {
        var aguardando = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando).Pedido;
        var comPagamento = new CobrancaPedidoFixture("entregue");
        comPagamento.AdicionarPagamento("pix-direto", 10m);

        var marcarAguardando = () => aguardando.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);
        var marcarComPagamento = () => comPagamento.Pedido.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);

        marcarAguardando.Should().Throw<InvalidOperationException>();
        marcarComPagamento.Should().Throw<InvalidOperationException>();
        aguardando.QuitadoHistorico.Should().BeFalse();
        comPagamento.Pedido.QuitadoHistorico.Should().BeFalse();
        var entregue = EntregueQuitadoNoHistorico().Pedido;
        entregue.QuitadoHistoricoEm.Should().Be(CobrancaPedidoFixture.Agora);
        entregue.TotalPago.Should().Be(0m, "a marca é a quitação: nenhum pagamento é criado");
    }

    [Fact]
    public async Task Registrar_pagamento_no_pedido_quitado_no_historico_e_recusado_e_nao_cria_nada()
    {
        var f = EntregueQuitadoNoHistorico();
        var quitacao = QuitacaoPedidoTeste.Criar(f.PedidoRepo, f.CobrancaRepo, f.ConversaRepo, f.ImpressaoRepo, f.Publicador, f.OperacaoEventos);
        var registrar = new RegistrarPagamentoPedidoUseCase(f.PedidoRepo, f.Uow,
            NullLogger<RegistrarPagamentoPedidoUseCase>.Instance, new CalculadoraInicioPrevistoPedido(f.PrazoQueries), quitacao);

        var pagar = () => registrar.ExecuteAsync(new RegistrarPagamentoPedidoCommand(f.EmpresaId, f.Pedido.Id, "dinheiro", 10m));

        (await pagar.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("quitado");
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.PedidoRepo.DidNotReceive().AddPagamentoAsync(Arg.Any<PedidoPagamento>());
    }

    [Fact]
    public async Task Confirmar_pagamento_do_provedor_no_pedido_quitado_no_historico_estorna_e_nao_registra()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.Pedido.Status = "entregue";
        f.Pedido.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);

        var r = await f.Confirmar().ExecuteAsync(new ConfirmarPagamentoPedidoInput(f.Pedido.Id, PagamentoExternoId: "pay-9",
            StatusPagamento: "approved", ValorPago: 25m, MetodoPagamentoExterno: "pix", TipoPagamentoExterno: "bank_transfer",
            PagoEm: CobrancaPedidoFixture.Agora));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync("pay-9", 25m, "estorno-duplicado-pay-9", Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task AReceber_do_pedido_quitado_no_historico_e_zero_e_o_pedido_comum_segue_a_conta_de_hoje()
    {
        var quitado = EntregueQuitadoNoHistorico();
        var comum = new CobrancaPedidoFixture("entregue");

        (await quitado.Saldos().AReceberAsync(quitado.Pedido, CancellationToken.None)).Should().Be(0m);
        (await comum.Saldos().AReceberAsync(comum.Pedido, CancellationToken.None))
            .Should().Be(25m, "sem a marca o entregue sem pagamento segue a receber");
    }

    // O que a tela lê: o detalhe do pedido (GET pedidos/{id}) já traz o que falta receber decidido pelo servidor.
    private static async Task<decimal?> AReceberDoDetalheAsync(CobrancaPedidoFixture f)
    {
        f.PedidoRepo.GetByIdWithDetailsAsync(f.EmpresaId, f.Pedido.Id).Returns(f.Pedido);
        var detalhe = await new ObterPedidoDetalhesUseCase(f.PedidoRepo)
            .ExecuteAsync(new ObterPedidoDetalhesQuery(f.EmpresaId, f.Pedido.Id));
        return detalhe!.Pedido.AReceber;
    }

    [Fact]
    public async Task Detalhe_do_pedido_quitado_no_historico_informa_que_nada_falta_receber()
    {
        var f = EntregueQuitadoNoHistorico();

        (await AReceberDoDetalheAsync(f)).Should().Be(0m, "o servidor decide: a tela não refaz total menos pago");
    }

    [Fact]
    public async Task Detalhe_do_pedido_comum_sem_pagamento_informa_o_total_a_receber()
    {
        var f = new CobrancaPedidoFixture("entregue");

        (await AReceberDoDetalheAsync(f)).Should().Be(25m, "sem a marca o entregue sem pagamento segue a receber");
    }

    [Fact]
    public async Task Detalhe_do_pedido_comum_com_pagamento_parcial_informa_o_que_falta()
    {
        var f = new CobrancaPedidoFixture("entregue");
        f.AdicionarPagamento("pix-direto", 10m);

        (await AReceberDoDetalheAsync(f)).Should().Be(15m);
    }
}
