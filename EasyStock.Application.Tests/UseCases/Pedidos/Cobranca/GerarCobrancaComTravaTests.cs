using EasyStock.Application.UseCases.Pedidos.Cobranca;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1724: quatro "gerar cobrança" simultâneos criaram quatro links pagáveis para o mesmo pedido no laboratório.
/// A corrida em si só se prova com banco real; aqui fica a ordem que a evita.
/// </summary>
public class GerarCobrancaComTravaTests
{
    [Fact]
    public async Task Reemissao_pelo_console_trava_o_pedido_na_transacao_antes_de_ler_as_cobrancas()
    {
        var f = new CobrancaPedidoFixture();

        var r = await f.Gerar().ExecuteComTravaAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        r.LinkPagamento.Should().NotBeNull();
        Received.InOrder(() =>
        {
            f.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<CobrancaPedidoResult>>>(), Arg.Any<CancellationToken>());
            f.PedidoRepo.TravarAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>());
            f.CobrancaRepo.ListarDoPedidoAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>());
        });
    }
}
