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

    /// <summary>
    /// #1717-8: o pedido mudou de valor e a reemissão cancela o link antigo. Sem expirar a preferência, o cliente
    /// ainda pagava o link velho e o pagamento virava "duplicado". A expiração sai depois que a transação fechou.
    /// </summary>
    [Fact]
    public async Task Reemissao_com_valor_novo_expira_a_preferencia_antiga_depois_da_transacao()
    {
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline(referencia: "pref-antiga", valor: 20m);
        var emTransacao = false;
        f.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<CobrancaPedidoResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                emTransacao = true;
                try { return await ci.Arg<Func<CancellationToken, Task<CobrancaPedidoResult>>>()(ci.Arg<CancellationToken>()); }
                finally { emTransacao = false; }
            });
        var expirouDentro = false;
        f.MpClient.When(m => m.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => expirouDentro |= emTransacao);

        var r = await f.Gerar().ExecuteComTravaAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        antiga.Status.Should().Be(Domain.Enums.Pagamentos.StatusCobrancaPedido.Cancelada);
        r.LinkPagamento.Should().Be("https://mp.test/pref-1");
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-antiga", CobrancaPedidoFixture.Agora, Arg.Any<CancellationToken>());
        expirouDentro.Should().BeFalse("a preferência só expira depois do commit real");
    }

    /// <summary>#1717-8: falha do provedor ao expirar não desfaz a reemissão (best-effort, só loga).</summary>
    [Fact]
    public async Task Falha_ao_expirar_nao_desfaz_a_reemissao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-antiga", valor: 20m);
        f.MpClient.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("mp fora"));

        var r = await f.Gerar().ExecuteComTravaAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        r.LinkPagamento.Should().Be("https://mp.test/pref-1");
    }

    /// <summary>#1717-8: a preferência a expirar é dado interno; não sai na resposta da API.</summary>
    [Fact]
    public void Preferencia_substituida_nao_e_serializada()
    {
        var r = new CobrancaPedidoResult(Guid.NewGuid(), Guid.NewGuid(), "mercadopago", "Pendente", "https://mp.test/x", 10m, null, 1, false)
            { PreferenciaSubstituida = "pref-antiga" };

        System.Text.Json.JsonSerializer.Serialize(r).Should().NotContain("pref-antiga");
    }
}
