using System.Net;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// I3.2 (#1688; invariantes 3, 4 e 5): a cobrança sabe de qual conta do Mercado Pago é. Pagamento de outra
/// conta não confirma, e credencial trocada ou ausente nunca vira "o cliente não pagou".
/// </summary>
public class CobrancaContaRecebedoraTests
{
    private const string ContaDaLoja = "724484980";
    private const string OutraConta = "999000111";

    private static CobrancaPedido Online(CobrancaPedidoFixture f, string? conta, int tentativa = 2)
    {
        var c = CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id, f.Pedido.Total.Valor, "pref-conta", "https://mp.test/pref-conta",
            CobrancaPedidoFixture.Agora.AddMinutes(30), tentativa, CobrancaPedidoFixture.Agora.AddMinutes(-1), contaRecebedoraId: conta);
        f.Cobrancas.Add(c);
        return c;
    }

    private static ConfirmarPagamentoPedidoInput Aprovado(CobrancaPedidoFixture f, string? contaDoPagamento) =>
        new(f.Pedido.Id, "20359978", PagamentoMercadoPago.Approved, f.Pedido.Total.Valor, "visa", "credit_card",
            CobrancaPedidoFixture.Agora, ContaRecebedoraId: contaDoPagamento);

    // ── Criação ────────────────────────────────────────────────────────

    [Fact]
    public async Task Cobranca_online_guarda_a_conta_e_a_origem_da_credencial()
    {
        var f = new CobrancaPedidoFixture();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PreferenceCriadaResult("pref-9", "https://mp.test/pref-9", ContaDaLoja));

        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, null, 1));

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.ContaRecebedoraId.Should().Be(ContaDaLoja);
        cobranca.OrigemCredencial.Should().Be(CobrancaPedido.OrigemCredencialGlobal);
    }

    // ── Confirmação ────────────────────────────────────────────────────

    [Fact]
    public async Task Pagamento_de_outra_conta_nao_confirma()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = Online(f, ContaDaLoja);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f, OutraConta));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.ContaDivergente);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Motivo.Should().StartWith("conta_divergente");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.Estorno.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Pagamento_sem_conta_informada_pela_fonte_confirma_como_antes(string? contaDoPagamento)
    {
        // A fonte (reconsulta no Mercado Pago) não disse a conta: não se sabe, então não se recusa dinheiro.
        var f = new CobrancaPedidoFixture();
        Online(f, ContaDaLoja);

        (await f.Confirmar().ExecuteAsync(Aprovado(f, contaDoPagamento))).Confirmado.Should().BeTrue();
    }

    [Fact]
    public async Task Notificacao_reentregue_de_outra_conta_nao_repete_o_evento_no_historico()
    {
        var f = new CobrancaPedidoFixture();
        Online(f, ContaDaLoja);

        await f.Confirmar().ExecuteAsync(Aprovado(f, OutraConta));
        var r = await f.Confirmar().ExecuteAsync(Aprovado(f, OutraConta));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.ContaDivergente);
        f.Eventos.Count(e => e.Tipo == "pagamento_divergente").Should().Be(1);
    }

    [Fact]
    public async Task Pagamento_da_mesma_conta_confirma()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = Online(f, ContaDaLoja);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f, ContaDaLoja));

        r.Confirmado.Should().BeTrue();
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OutraConta)]
    public async Task Cobranca_antiga_sem_conta_confirma_como_antes(string? contaDoPagamento)
    {
        var f = new CobrancaPedidoFixture();
        Online(f, conta: null);

        (await f.Confirmar().ExecuteAsync(Aprovado(f, contaDoPagamento))).Confirmado.Should().BeTrue();
    }

    // ── Cobrança vencida ───────────────────────────────────────────────

    private static CobrancaPedidoVencida Vencida(CobrancaPedidoFixture f, CobrancaPedido c) =>
        new(c.Id, f.EmpresaId, f.Pedido.Id, c.ContaRecebedoraId);

    private static CobrancaPedidoFixture ComLinkVencido()
    {
        var f = new CobrancaPedidoFixture();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        return f;
    }

    private static async Task NaoCancelouAsync(CobrancaPedidoFixture f, CobrancaPedido cobranca, ResultadoExpiracaoCobranca r)
    {
        r.Should().Be(ResultadoExpiracaoCobranca.AguardandoCredencial);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.AguardandoCredencial.Should().BeTrue();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        await f.VagaRepo.DidNotReceiveWithAnyArgs().LiberarPorPedidoAsync(default, default!, default);
        await f.MpClient.DidNotReceive().BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cobranca_pendente_com_credencial_trocada_nao_cancela()
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, ContaDaLoja);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns(OutraConta);

        var r = await f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca));

        await NaoCancelouAsync(f, cobranca, r);
    }

    [Fact]
    public async Task Cobranca_pendente_sem_credencial_nao_cancela()
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, ContaDaLoja);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MercadoPagoNaoConfiguradoException());

        await NaoCancelouAsync(f, cobranca, await f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Cobranca_pendente_com_credencial_recusada_nao_cancela(HttpStatusCode status)
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, conta: null); // vale também para cobrança antiga, sem conta gravada
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("credencial", null, status));

        await NaoCancelouAsync(f, cobranca, await f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca)));
    }

    [Fact]
    public async Task Falha_passageira_do_provedor_propaga_e_o_job_tenta_de_novo()
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, ContaDaLoja);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("mp fora", null, HttpStatusCode.BadGateway));

        var act = () => f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca));

        await act.Should().ThrowAsync<HttpRequestException>();
        cobranca.AguardandoCredencial.Should().BeFalse("instabilidade do provedor não é falta de credencial");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task Mesma_conta_segue_o_fluxo_de_expiracao()
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, ContaDaLoja);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns(ContaDaLoja);

        var r = await f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca));

        r.Should().Be(ResultadoExpiracaoCobranca.PedidoCancelado);
        cobranca.AguardandoCredencial.Should().BeFalse();
    }

    [Fact]
    public async Task Pagamento_perdido_de_outra_conta_nao_confirma_pela_consulta()
    {
        var f = ComLinkVencido();
        var cobranca = Online(f, ContaDaLoja);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns(ContaDaLoja);
        f.PagamentosNoMercadoPago.Add(new PagamentoMercadoPago("20359978", PagamentoMercadoPago.Approved, null,
            f.Pedido.Id.ToString(), f.Pedido.Total.Valor, CobrancaPedidoFixture.Agora, "visa", "credit_card", CollectorId: OutraConta));

        var r = await f.ProcessarVencida().ExecuteAsync(Vencida(f, cobranca));

        r.Should().NotBe(ResultadoExpiracaoCobranca.ConfirmadaPelaConsulta);
        cobranca.Status.Should().NotBe(StatusCobrancaPedido.Paga);
    }

    // ── Volta quando a credencial volta ────────────────────────────────

    [Fact]
    public async Task Credencial_de_volta_devolve_as_cobrancas_dessa_conta_para_a_varredura()
    {
        var mp = Substitute.For<IMercadoPagoClient>();
        var repo = Substitute.For<ICobrancaPedidoRepository>();
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns(ContaDaLoja);
        repo.ExisteAguardandoCredencialAsync(Arg.Any<CancellationToken>()).Returns(true);
        repo.RetomarAguardandoCredencialAsync(ContaDaLoja, Arg.Any<CancellationToken>()).Returns(3);

        var retomadas = await new RetomarCobrancasAguardandoCredencialUseCase(mp, repo,
            NullLogger<RetomarCobrancasAguardandoCredencialUseCase>.Instance).ExecuteAsync();

        retomadas.Should().Be(3);
    }

    [Fact]
    public async Task Sem_credencial_ninguem_volta_para_a_varredura()
    {
        var mp = Substitute.For<IMercadoPagoClient>();
        var repo = Substitute.For<ICobrancaPedidoRepository>();
        repo.ExisteAguardandoCredencialAsync(Arg.Any<CancellationToken>()).Returns(true);
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new MercadoPagoNaoConfiguradoException());

        var retomadas = await new RetomarCobrancasAguardandoCredencialUseCase(mp, repo,
            NullLogger<RetomarCobrancasAguardandoCredencialUseCase>.Instance).ExecuteAsync();

        retomadas.Should().Be(0);
        await repo.DidNotReceiveWithAnyArgs().RetomarAguardandoCredencialAsync(default!, default);
    }

    [Fact]
    public async Task Sem_ninguem_aguardando_o_provedor_nem_e_consultado()
    {
        var mp = Substitute.For<IMercadoPagoClient>();
        var repo = Substitute.For<ICobrancaPedidoRepository>();

        var retomadas = await new RetomarCobrancasAguardandoCredencialUseCase(mp, repo,
            NullLogger<RetomarCobrancasAguardandoCredencialUseCase>.Instance).ExecuteAsync();

        retomadas.Should().Be(0);
        await mp.DidNotReceive().ObterContaRecebedoraAsync(Arg.Any<CancellationToken>());
    }

    // ── Domínio ────────────────────────────────────────────────────────

    [Fact]
    public void Aguardar_credencial_vale_so_para_cobranca_online_pendente_e_e_desfeito_ao_pagar()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = Online(f, ContaDaLoja);

        cobranca.AguardarCredencial(CobrancaPedidoFixture.Agora);
        cobranca.AguardarCredencial(CobrancaPedidoFixture.Agora.AddMinutes(5));

        cobranca.AguardandoCredencialDesde.Should().Be(CobrancaPedidoFixture.Agora, "a primeira marca fica");
        cobranca.MarcarPaga("20359978", cobranca.Valor, "pix", CobrancaPedidoFixture.Agora.AddMinutes(9));
        cobranca.AguardandoCredencial.Should().BeFalse();
    }
}