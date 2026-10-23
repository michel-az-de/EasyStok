using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1717-8: a baixa manual que quita o pedido cancela o link online pendente. Sem expirar a preferência, o cliente
/// ainda pagava o link e o dinheiro entrava em dobro. A expiração sai depois que a transação fechou.
/// </summary>
public class QuitacaoManualLinkPendenteTests
{
    [Fact]
    public async Task Quitacao_manual_cancela_o_link_e_expira_a_preferencia_depois_da_transacao()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var link = f.AdicionarOnline(referencia: "pref-pendente");
        var emTransacao = false;
        f.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<PedidoResult?>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                emTransacao = true;
                try { return await ci.Arg<Func<CancellationToken, Task<PedidoResult?>>>()(ci.Arg<CancellationToken>()); }
                finally { emTransacao = false; }
            });
        var expirouDentro = false;
        f.MpClient.When(m => m.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => expirouDentro |= emTransacao);

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "dinheiro", 25m));

        link.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-pendente", CobrancaPedidoFixture.Agora, Arg.Any<CancellationToken>());
        expirouDentro.Should().BeFalse("a preferência só expira depois do commit real");
    }

    [Fact]
    public async Task Pagamento_parcial_nao_cancela_nem_expira_o_link()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var link = f.AdicionarOnline(referencia: "pref-pendente");

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "dinheiro", 10m));

        link.Status.Should().Be(StatusCobrancaPedido.Pendente);
        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }
}
