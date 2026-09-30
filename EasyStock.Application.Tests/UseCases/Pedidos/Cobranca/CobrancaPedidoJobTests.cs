using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11: regra do <c>CobrancaPedidoJob</c> (a cada 60 s), que vive em <see cref="ProcessarCobrancaVencidaUseCase"/>.
/// Expirada na tentativa 1 com conversa: gera a tentativa 2 e manda o link. Tentativa 2, ou pedido sem
/// conversa (site): cancela o pedido com motivo <c>pagamento_expirado</c> e libera a vaga.
/// </summary>
public class CobrancaPedidoJobTests
{
    private static CobrancaPedidoVencida Item(CobrancaPedidoFixture f, Guid cobrancaId) =>
        new(cobrancaId, f.EmpresaId, f.Pedido.Id);

    [Fact]
    public async Task PrimeiraExpiracaoComConversaReemite()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 1, conversaId: Guid.NewGuid());

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.Reemitida);
        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
        var nova = f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente).Subject;
        nova.Tentativa.Should().Be(2);
        nova.ConversaId.Should().Be(vencida.ConversaId);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        await f.ConversaRepo.Received().ObterPorIdAsync(f.EmpresaId, vencida.ConversaId!.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SegundaExpiracaoCancela()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 2, conversaId: Guid.NewGuid());

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.PedidoCancelado);
        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        f.Eventos.Should().Contain(e => e.Tipo == "cancelado" && e.Detalhes == "pagamento_expirado");
        await f.VagaRepo.Received(1).LiberarPorPedidoAsync(f.Pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task SemConversaCancelaNaPrimeira()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 1, conversaId: null);

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.PedidoCancelado);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        await f.VagaRepo.Received(1).LiberarPorPedidoAsync(f.Pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PedidoJaNaFila_SoExpiraACobranca()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 2);

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.Expirada);
        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "pedido na fila não é cancelado pelo job");
    }

    [Fact]
    public async Task AindaNaoVenceu_Ignora()
    {
        var f = new CobrancaPedidoFixture();
        var pendente = f.AdicionarOnline();

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, pendente.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.Ignorada);
        pendente.Status.Should().Be(StatusCobrancaPedido.Pendente);
    }

    /// <summary>S32: webhook perdido. Antes de expirar, o job consulta <c>payments/search</c> e confirma.</summary>
    [Fact]
    public async Task VencidaComPagamentoAprovadoNoMercadoPago_ConfirmaEmVezDeExpirar()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 2);
        f.PagamentosNoMercadoPago.Add(new PagamentoMercadoPago(
            "pay-perdido", "approved", "accredited", f.Pedido.Id.ToString(), 25m,
            CobrancaPedidoFixture.Agora.AddMinutes(10), "pix", "bank_transfer"));

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.ConfirmadaPelaConsulta);
        vencida.Status.Should().Be(StatusCobrancaPedido.Paga);
        vencida.PagamentoExternoId.Should().Be("pay-perdido");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.Pagamentos.Should().ContainSingle(p => p.Referencia == "pay-perdido");
        await f.MpClient.Received(1).BuscarPagamentosPorReferenciaAsync(f.Pedido.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VencidaSoComPagamentoRecusado_SegueExpirando()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 2);
        f.PagamentosNoMercadoPago.Add(new PagamentoMercadoPago(
            "pay-x", "rejected", "cc_rejected_other_reason", f.Pedido.Id.ToString(), 25m, null, "visa", "credit_card"));

        var r = await f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        r.Should().Be(ResultadoExpiracaoCobranca.PedidoCancelado);
        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
    }

    [Fact]
    public async Task ConsultaAoMercadoPagoFalha_NaoExpiraNemCancela()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var vencida = f.AdicionarOnline(tentativa: 2);
        f.MpClient.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PagamentoMercadoPago>>>(_ => throw new HttpRequestException("mp fora"));

        var act = () => f.ProcessarVencida().ExecuteAsync(Item(f, vencida.Id));

        await act.Should().ThrowAsync<HttpRequestException>("sem saber se pagou, o job tenta de novo na próxima rodada");
        vencida.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }
}
