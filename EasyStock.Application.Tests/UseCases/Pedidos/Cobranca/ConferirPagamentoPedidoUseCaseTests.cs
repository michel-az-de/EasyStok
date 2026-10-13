using System.Net;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

public class ConferirPagamentoPedidoUseCaseTests
{
    [Theory]
    [InlineData(StatusPedidoMapper.Aguardando)]
    [InlineData(StatusPedidoMapper.Preparando)]
    public async Task BaixaManualDireta_LinkPendenteMesmoOperacional_NaoRecebe(string status)
    {
        var f = new CobrancaPedidoFixture(status);
        f.AdicionarOnline();
        var registrar = new RegistrarPagamentoPedidoUseCase(f.PedidoRepo, f.Uow,
            NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(f.PrazoQueries),
            QuitacaoPedidoTeste.Criar(f.PedidoRepo, f.CobrancaRepo, f.ConversaRepo,
                f.ImpressaoRepo, f.Publicador, f.OperacaoEventos));
        var receber = () => registrar.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 10m));
        await receber.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Confira o pagamento*");
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Cobrancas.Should().ContainSingle().Which.Status.Should().Be(StatusCobrancaPedido.Pendente);
        await f.MpClient.DidNotReceiveWithAnyArgs().BuscarPagamentosPorReferenciaAsync(default!, default);
    }

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
    [InlineData(null, "nao_encontrado")]
    [InlineData("pending", "em_processamento")]
    [InlineData("in_process", "em_processamento")]
    public async Task SemAprovacao_NaoBaixaNemTrocaParaManual(string? status, string situacao)
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline();
        if (status is not null) f.PagamentosNoMercadoPago.Add(Pagamento(f, status));
        (await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id))
            .Should().Be(new ConferirPagamentoPedidoResult(situacao, false));
        var trocar = () => f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "na_entrega"));
        await trocar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*cobrança online pendente*");
        f.Cobrancas.Should().ContainSingle().Which.Should().BeSameAs(online);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinalJaPago_ComplementoPendente_AprovacaoAntigaNaoConfirmaSaldo(bool saldoAprovado)
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var sinal = EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id,
            12.50m, "pref-sinal", "https://mp.test/sinal", CobrancaPedidoFixture.Agora.AddMinutes(30), 1,
            CobrancaPedidoFixture.Agora, contaRecebedoraId: "conta-original");
        sinal.MarcarPaga("pagamento-sinal", 12.50m, "pix", CobrancaPedidoFixture.Agora);
        f.AdicionarPagamento("pagamento-sinal", 12.50m);
        var saldo = EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id,
            12.50m, "pref-saldo", "https://mp.test/saldo", CobrancaPedidoFixture.Agora.AddMinutes(30), 2,
            CobrancaPedidoFixture.Agora, contaRecebedoraId: "conta-original");
        saldo.MarcarComplementar();
        f.Cobrancas.AddRange([sinal, saldo]);
        f.MpClient.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-original");
        f.PagamentosNoMercadoPago.Add(new("pagamento-sinal", "approved", null, f.Pedido.Id.ToString(),
            12.50m, CobrancaPedidoFixture.Agora, "pix", "bank_transfer", CollectorId: "conta-original"));
        if (saldoAprovado)
            f.PagamentosNoMercadoPago.Add(new("pagamento-saldo", "approved", null, f.Pedido.Id.ToString(),
                12.50m, CobrancaPedidoFixture.Agora, "pix", "bank_transfer", CollectorId: "conta-original"));

        var resultado = await f.Conferir().ExecuteAsync(f.EmpresaId, f.Pedido.Id);

        resultado.Should().Be(saldoAprovado
            ? new ConferirPagamentoPedidoResult("confirmado", true)
            : new ConferirPagamentoPedidoResult("nao_conciliado", false));
        f.Pedido.TotalPago.Should().Be(saldoAprovado ? 25m : 12.50m);
        f.Pedido.Pagamentos.Should().HaveCount(saldoAprovado ? 2 : 1);
        saldo.Status.Should().Be(saldoAprovado ? StatusCobrancaPedido.Paga : StatusCobrancaPedido.Pendente);
        sinal.PagamentoExternoId.Should().Be("pagamento-sinal");
        await f.MpClient.Received(1).BuscarPagamentosPorReferenciaAsync(f.Pedido.Id.ToString(), Arg.Any<CancellationToken>());
        await f.MpClient.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
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
