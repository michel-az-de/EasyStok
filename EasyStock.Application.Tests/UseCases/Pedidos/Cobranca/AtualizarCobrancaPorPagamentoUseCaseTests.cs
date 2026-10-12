using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S32: desfechos do pagamento do Mercado Pago que não confirmam o pedido. <c>refunded</c> e
/// <c>charged_back</c> marcam a cobrança paga como estornada; <c>rejected</c> e <c>cancelled</c> gravam o
/// motivo na cobrança e avisam a conversa uma vez; o status do pedido nunca muda aqui.
/// </summary>
public class AtualizarCobrancaPorPagamentoUseCaseTests
{
    private static AtualizarCobrancaPorPagamentoInput Input(Guid pedidoId, string status, string pagamento = "pay-9") =>
        new(pedidoId, pagamento, status);

    [Fact]
    public async Task EstornoMarcaCobrancaPaga()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var cobranca = f.AdicionarOnline();
        cobranca.MarcarPaga("pay-9", 25m, "credito", CobrancaPedidoFixture.Agora);

        var r = await f.AtualizarPorPagamento().ExecuteAsync(Input(f.Pedido.Id, "refunded"));

        r.Should().Be(SituacaoAtualizacaoCobranca.Estornada);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Estornada);
        cobranca.Motivo.Should().Contain("pay-9");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "o estorno não mexe no status; ocorrência é S27");
        f.Eventos.Should().ContainSingle(e => e.Tipo == "pagamento_estornado");
        f.Tenant.Received().SetCurrentTenant(f.EmpresaId);
    }

    [Fact]
    public async Task EstornoRepetidoNoOp()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var cobranca = f.AdicionarOnline();
        cobranca.MarcarPaga("pay-9", 25m, "credito", CobrancaPedidoFixture.Agora);
        var uc = f.AtualizarPorPagamento();
        await uc.ExecuteAsync(Input(f.Pedido.Id, "refunded"));

        var r = await uc.ExecuteAsync(Input(f.Pedido.Id, "charged_back"));

        r.Should().Be(SituacaoAtualizacaoCobranca.JaRegistrado);
        f.Eventos.Should().ContainSingle(e => e.Tipo == "pagamento_estornado");
    }

    [Fact]
    public async Task RecusadoGravaMotivoEAvisaConversa()
    {
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();
        var cobranca = f.AdicionarOnline(conversaId: conversaId);

        var r = await f.AtualizarPorPagamento().ExecuteAsync(Input(f.Pedido.Id, "rejected"));

        r.Should().Be(SituacaoAtualizacaoCobranca.Recusada);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente, "o cliente ainda pode pagar pelo mesmo link");
        cobranca.Motivo.Should().Be("pagamento_recusado: pagamento pay-9 (rejected)");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecusadoRepetidoNaoAvisaDeNovo()
    {
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();
        f.AdicionarOnline(conversaId: conversaId);
        var uc = f.AtualizarPorPagamento();
        await uc.ExecuteAsync(Input(f.Pedido.Id, "rejected"));

        var r = await uc.ExecuteAsync(Input(f.Pedido.Id, "rejected"));

        r.Should().Be(SituacaoAtualizacaoCobranca.JaRegistrado);
        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PixVencidoCancelado_NaoAvisaTentarDeNovo()
    {
        // #1289: o Mercado Pago cancela o Pix no instante do vencimento; o job (60 s) ainda não expirou a cobrança.
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();
        var cobranca = f.AdicionarOnline(conversaId: conversaId, expiraEm: CobrancaPedidoFixture.Agora);

        var r = await f.AtualizarPorPagamento().ExecuteAsync(Input(f.Pedido.Id, "cancelled"));

        r.Should().Be(SituacaoAtualizacaoCobranca.Recusada);
        cobranca.Motivo.Should().Contain("cancelled");
        await f.ConversaRepo.DidNotReceive().ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatusDetalheExpirado_NaoAvisaTentarDeNovo()
    {
        // O relógio do Mercado Pago pode estar à frente do nosso: status_detail = expired basta.
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();
        f.AdicionarOnline(conversaId: conversaId);

        var r = await f.AtualizarPorPagamento().ExecuteAsync(
            Input(f.Pedido.Id, "cancelled") with { StatusDetalhe = "expired" });

        r.Should().Be(SituacaoAtualizacaoCobranca.Recusada);
        await f.ConversaRepo.DidNotReceive().ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PedidoSemCobranca_Ignora()
    {
        var f = new CobrancaPedidoFixture();

        var r = await f.AtualizarPorPagamento().ExecuteAsync(Input(Guid.NewGuid(), "rejected"));

        r.Should().Be(SituacaoAtualizacaoCobranca.SemCobranca);
        await f.Uow.DidNotReceive().CommitAsync();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("in_process")]
    [InlineData("approved")]
    public async Task StatusSemDesfecho_Ignora(string status)
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = f.AdicionarOnline();

        var r = await f.AtualizarPorPagamento().ExecuteAsync(Input(f.Pedido.Id, status));

        r.Should().Be(SituacaoAtualizacaoCobranca.Ignorada);
        cobranca.Motivo.Should().BeNull();
    }
}
