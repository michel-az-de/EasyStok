using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases.Pedidos;

/// <summary>
/// E4 L1 (#3076): o detalhe do pedido informa o que falta receber pela mesma conta da baixa
/// (<see cref="EasyStock.Application.Services.Pedidos.SaldoFinanceiroPedido"/>), com cupom e quitado do histórico.
/// </summary>
public class ObterPedidoDetalhesAReceberTests
{
    private static CobrancaPedidoFixture PedidoDeCem()
    {
        var f = new CobrancaPedidoFixture("preparando");
        f.Pedido.Total = Dinheiro.FromDecimal(100m);
        f.Creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 0m));
        f.PedidoRepo.GetByIdWithDetailsAsync(f.EmpresaId, f.Pedido.Id).Returns(f.Pedido);
        return f;
    }

    private static async Task<EasyStock.Application.UseCases.Pedidos.PedidoResult> DetalheAsync(CobrancaPedidoFixture f, bool comSaldos) =>
        (await new ObterPedidoDetalhesUseCase(f.PedidoRepo, comSaldos ? f.Saldos() : null)
            .ExecuteAsync(new ObterPedidoDetalhesQuery(f.EmpresaId, f.Pedido.Id)))!.Pedido;

    [Fact]
    public async Task Pedido_de_100_com_cupom_de_20_aplicado_e_sem_pagamento_falta_receber_80()
    {
        var f = PedidoDeCem();
        f.Creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 20m));

        (await DetalheAsync(f, comSaldos: true)).AReceber.Should().Be(80m);
    }

    [Fact]
    public async Task Pedido_quitado_no_historico_nao_tem_nada_a_receber_no_detalhe()
    {
        var f = new CobrancaPedidoFixture("entregue");
        f.Pedido.Total = Dinheiro.FromDecimal(100m);
        f.Creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 0m));
        f.Pedido.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);

        (await DetalheAsync(f, comSaldos: true)).AReceber.Should().Be(0m);
    }

    [Fact]
    public async Task Sem_o_saldo_financeiro_o_detalhe_segue_com_a_conta_pelos_pagamentos()
    {
        var f = PedidoDeCem();
        f.Creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 20m));

        (await DetalheAsync(f, comSaldos: false)).AReceber.Should().Be(f.Pedido.AReceberPelosPagamentos).And.Be(100m);
    }

    [Fact]
    public async Task Detalhe_traz_o_endereco_de_entrega()
    {
        var f = PedidoDeCem();
        f.Pedido.EnderecoEntrega = "Rua A, 10";

        (await DetalheAsync(f, comSaldos: false)).EnderecoEntrega.Should().Be("Rua A, 10");
    }
}
