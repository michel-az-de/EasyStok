using System.Net;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

public class ConferirPagamentoPedidoUseCaseTests
{
    [Fact]
    public async Task Aprovado_ConciliaERepeticaoNaoRecebeDeNovo()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.PagamentosNoMercadoPago.Add(Pagamento(f, "approved"));
        var primeiro = await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id);
        var repetido = await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id);
        primeiro.Should().Be(new ConferirPagamentoPedidoResult("confirmado", true));
        repetido.Should().Be(new ConferirPagamentoPedidoResult("ja_conferido", true));
        f.Pedido.Pagamentos.Should().ContainSingle();
        f.Pedido.TotalPago.Should().Be(25m);
        await f.MpClient.Received(1).BuscarPagamentosPorReferenciaAsync(f.Pedido.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("pending", "em_processamento")]
    [InlineData("in_process", "em_processamento")]
    public async Task EmProcessamento_NaoBaixaNemTrocaParaManual(string status, string situacao)
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline();
        f.PagamentosNoMercadoPago.Add(Pagamento(f, status));
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id))
            .Should().Be(new ConferirPagamentoPedidoResult(situacao, false));
        var trocar = () => f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "na_entrega"));
        await trocar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*cobrança online pendente*processando*");
        f.Cobrancas.Should().ContainSingle().Which.Should().BeSameAs(online);
        online.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    /// <summary>
    /// #1726 convive com #1733: sem pagamento no provedor, liberar sem pagamento (ou "Marcar pago") ainda
    /// cancela o link e conserva a dívida na entrega; o que muda é que a consulta vem antes do cancelamento.
    /// </summary>
    [Fact]
    public async Task SemPagamentoNoProvedor_TrocaParaManualCancelaOLinkDepoisDeConferir()
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline(referencia: "pref-1733");

        var r = await f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await f.MpClient.Received(1).BuscarPagamentosPorReferenciaAsync(f.Pedido.Id.ToString(), Arg.Any<CancellationToken>());
        online.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Cobranca.Provedor.Should().Be(EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.ProvedorNaEntrega);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-1733", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("aguardando_credencial")]
    [InlineData("nao_conciliado")]
    public async Task CredencialQueNaoAlcancaOuPagamentoSemConciliar_NaoTrocaParaManual(string situacao)
    {
        var f = new CobrancaPedidoFixture();
        var online = EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id,
            25m, "pref-1733", "https://mp.test/1733", CobrancaPedidoFixture.Agora.AddMinutes(30), 1,
            CobrancaPedidoFixture.Agora, contaRecebedoraId: "conta-original");
        f.Cobrancas.Add(online);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
            .Returns(situacao == "aguardando_credencial" ? "outra-conta" : "conta-original");
        f.PagamentosNoMercadoPago.Add(Pagamento(f, "approved") with { CollectorId = "outra-conta" });

        var trocar = () => f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await trocar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*cobrança online pendente*");
        online.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TenantDivergenteOuCancelado_NaoConsulta(bool tenantDivergente)
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        if (!tenantDivergente) f.Pedido.Status = StatusPedidoMapper.Cancelado;
        var consultar = () => f.Conferir().ExecuteAsync(tenantDivergente ? Guid.NewGuid() : f.EmpresaId, f.Pedido.Id);
        if (tenantDivergente) await consultar.Should().ThrowAsync<CobrancaPedidoNaoEncontradoException>();
        else await consultar.Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await f.MpClient.DidNotReceiveWithAnyArgs().BuscarPagamentosPorReferenciaAsync(default!, default);
        await f.MpClient.DidNotReceiveWithAnyArgs().ObterContaRecebedoraAsync(default);
    }

    [Theory]
    [InlineData(401, "aguardando_credencial")]
    [InlineData(403, "aguardando_credencial")]
    public async Task CredencialRecusada_NaoInterpretaComoPagamentoAusente(int status, string situacao)
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("credencial", null, (HttpStatusCode)status));
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id)).Situacao.Should().Be(situacao);
        await f.MpClient.DidNotReceiveWithAnyArgs().BuscarPagamentosPorReferenciaAsync(default!, default);
    }

    [Fact]
    public async Task ProvedorIndisponivel_PreservaCobrancaENaoBaixa()
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline();
        f.MpClient.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PagamentoMercadoPago>>>(_ => throw new HttpRequestException("indisponivel"));
        var trocar = () => f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "na_entrega"));
        await trocar.Should().ThrowAsync<MercadoPagoIndisponivelException>();
        online.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Cobrancas.Should().ContainSingle();
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task SemLink_NaoConsultaProvedor()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarNaEntrega();
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id)).Situacao.Should().Be("sem_cobranca_online");
        await f.MpClient.DidNotReceiveWithAnyArgs().ObterContaRecebedoraAsync(default);
    }

    [Theory]
    [InlineData(true, "aguardando_credencial")]
    [InlineData(false, "nao_conciliado")]
    public async Task ContaOriginalPreservada_CredencialOuCollectorDivergenteNaoBaixa(bool credencialDivergente, string situacao)
    {
        var f = new CobrancaPedidoFixture();
        f.Cobrancas.Add(EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id,
            25m, "pref-1733", "https://mp.test/1733", CobrancaPedidoFixture.Agora.AddMinutes(30), 1,
            CobrancaPedidoFixture.Agora, contaRecebedoraId: "conta-original"));
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
            .Returns(credencialDivergente ? "outra-conta" : "conta-original");
        f.PagamentosNoMercadoPago.Add(Pagamento(f, "approved") with { CollectorId = "outra-conta" });
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id)).Situacao.Should().Be(situacao);
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Cobrancas.Should().ContainSingle().Which.Status.Should().Be(StatusCobrancaPedido.Pendente);
        if (credencialDivergente)
            await f.MpClient.DidNotReceiveWithAnyArgs().BuscarPagamentosPorReferenciaAsync(default!, default);
    }

    private static PagamentoMercadoPago Pagamento(CobrancaPedidoFixture f, string status) =>
        new("pagamento-1733", status, null, f.Pedido.Id.ToString(), 25m, CobrancaPedidoFixture.Agora, "pix", "bank_transfer");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CredencialAusenteAntesOuDuranteBusca_PreservaBloqueio(bool antesDaBusca)
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        if (antesDaBusca)
            f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>())
                .Returns<string>(_ => throw new MercadoPagoNaoConfiguradoException());
        else
            f.MpClient.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Task<IReadOnlyList<PagamentoMercadoPago>>>(_ => throw new MercadoPagoNaoConfiguradoException());
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id))
            .Should().Be(new ConferirPagamentoPedidoResult("aguardando_credencial", false));
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Cobrancas.Should().ContainSingle().Which.Status.Should().Be(StatusCobrancaPedido.Pendente);
    }
}
