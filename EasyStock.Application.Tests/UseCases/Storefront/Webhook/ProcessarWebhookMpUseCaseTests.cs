using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Webhook;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Webhook;

/// <summary>
/// Tests do <see cref="ProcessarWebhookMpUseCase"/> — ADR-0006 §Process:
/// chama MP como fonte da verdade, aplica transição em Pedido, publica
/// eventos de Outbox.
/// </summary>
public class ProcessarWebhookMpUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private sealed record Fakes(
        IWebhookProcessadoRepository WebhookRepo,
        IMercadoPagoClient MpClient,
        IPedidoStorefrontRepository PedidoRepo,
        IVagaOcupadaRepository VagaRepo,
        IPublicadorEventos Publicador,
        ProcessarWebhookMpUseCase UseCase);

    private static Fakes BuildFakes()
    {
        var webhookRepo = Substitute.For<IWebhookProcessadoRepository>();
        var mpClient = Substitute.For<IMercadoPagoClient>();
        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        var publicador = Substitute.For<IPublicadorEventos>();

        var useCase = new ProcessarWebhookMpUseCase(
            webhookRepo, mpClient, pedidoRepo, vagaRepo, publicador,
            NullLogger<ProcessarWebhookMpUseCase>.Instance);

        return new Fakes(webhookRepo, mpClient, pedidoRepo, vagaRepo, publicador, useCase);
    }

    private static WebhookProcessado WebhookReceived(string eventoId = "payment-123")
    {
        return WebhookProcessado.Receber(
            "mercadopago", eventoId, "payment.updated",
            $"{{\"data\":{{\"id\":\"{eventoId}\"}}}}");
    }

    private static Pedido PedidoAguardandoPagamento(Guid? pedidoId = null)
    {
        var p = Pedido.Criar(EmpresaId, origem: "storefront");
        var id = pedidoId ?? Guid.NewGuid();
        p.Id = id;
        p.Status = StatusPedidoMapper.AguardandoPagamento;
        p.ClienteId = Guid.NewGuid();
        return p;
    }

    // ── Approved ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Processar_StatusApproved_TransicionaPedidoParaAguardandoAprovacaoBaba_E_PublicaEvento()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "approved", null, pedido.Id.ToString(), 150m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Aprovado);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Processed);
        webhook.EmpresaId.Should().Be(EmpresaId);

        await f.Publicador.Received(1).PublicarAsync(
            Arg.Is<NotificarBabaPedidoNovoEvent>(e =>
                e.PedidoId == pedido.Id && e.EmpresaId == EmpresaId));
        await f.WebhookRepo.Received(1).UpdateAsync(webhook, Arg.Any<CancellationToken>());
        await f.PedidoRepo.Received(1).UpdateAsync(pedido, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Processar_StatusApproved_PedidoJaEmEstadoAvancado_NaoRegredeENaoPublicaDuasVezes()
    {
        // Cenário: webhook duplicado / reentrante. Pedido já passou de AguardandoPagamento.
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();
        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "approved", null, pedido.Id.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Aprovado);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba); // não regrediu
        webhook.Status.Should().Be(WebhookProcessadoStatus.Processed);
        await f.Publicador.DidNotReceive().PublicarAsync(Arg.Any<NotificarBabaPedidoNovoEvent>());
    }

    // ── Rejected / Cancelled ───────────────────────────────────────────────

    [Theory]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    [InlineData("refunded")]
    [InlineData("charged_back")]
    public async Task Processar_StatusRejectedOuEquivalente_CancelaPedido_E_LiberaVaga(string statusMp)
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, statusMp, "cc_rejected_insufficient_amount", pedido.Id.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        f.VagaRepo.LiberarPorPedidoAsync(pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Recusado);
        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedido.CanceladoEm.Should().NotBeNull();
        webhook.Status.Should().Be(WebhookProcessadoStatus.Processed);

        await f.VagaRepo.Received(1).LiberarPorPedidoAsync(pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.Publicador.Received(1).PublicarAsync(Arg.Any<PedidoCanceladoEvent>());
        await f.Publicador.Received(1).PublicarAsync(
            Arg.Is<NotificarClientePagamentoRecusadoEvent>(e =>
                e.PedidoId == pedido.Id && e.MotivoRecusa.Contains("cc_rejected_insufficient_amount")));
    }

    [Fact]
    public async Task Processar_StatusRejected_PedidoJaCancelado_NaoLiberaDuasVezesENaoPublicaEvento()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();
        pedido.Status = StatusPedidoMapper.Cancelado;
        pedido.CanceladoEm = DateTime.UtcNow.AddMinutes(-1);

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "rejected", null, pedido.Id.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Recusado);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Processed);
        await f.VagaRepo.DidNotReceive().LiberarPorPedidoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.Publicador.DidNotReceive().PublicarAsync(Arg.Any<PedidoCanceladoEvent>());
    }

    // ── Pending ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("pending")]
    [InlineData("in_process")]
    [InlineData("authorized")]
    public async Task Processar_StatusPendente_MantemWebhookReceivedENaoMudaPedido(string statusMp)
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, statusMp, null, pedido.Id.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Pendente);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Received);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        await f.WebhookRepo.DidNotReceive().UpdateAsync(webhook, Arg.Any<CancellationToken>());
        await f.PedidoRepo.DidNotReceive().UpdateAsync(pedido, Arg.Any<CancellationToken>());
    }

    // ── Orphan ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Processar_ExternalReferenceNaoEhGuid_MarcaOrfao()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "approved", null, "nao-eh-guid", 100m));

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Orfao);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Orphan);
        webhook.Motivo.Should().Contain("external_reference inválida");
        await f.WebhookRepo.Received(1).UpdateAsync(webhook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Processar_PedidoInexistente_MarcaOrfao_E_NaoRetenta()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedidoIdInexistente = Guid.NewGuid();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "approved", null, pedidoIdInexistente.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedidoIdInexistente, Arg.Any<CancellationToken>()).Returns((Pedido?)null);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Orfao);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Orphan);
        webhook.Motivo.Should().Contain(pedidoIdInexistente.ToString());
    }

    // ── Erro ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Processar_GetPaymentLanca_MarcaWebhookComoErro()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("MP timeout"));

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Erro);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Error);
        webhook.Motivo.Should().Contain("GetPayment falhou");
        await f.WebhookRepo.Received(1).UpdateAsync(webhook, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Processar_StatusDesconhecido_MarcaErro()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        var pedido = PedidoAguardandoPagamento();

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);
        f.MpClient.GetPaymentAsync(webhook.EventoId, Arg.Any<CancellationToken>()).Returns(
            new MpPaymentDetailsDto(webhook.EventoId, "marciano-status", null, pedido.Id.ToString(), 100m));
        f.PedidoRepo.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Erro);
        webhook.Status.Should().Be(WebhookProcessadoStatus.Error);
        webhook.Motivo.Should().Contain("marciano-status");
    }

    // ── Idempotência: já processado ───────────────────────────────────────

    [Fact]
    public async Task Processar_WebhookJaProcessed_RetornaJaProcessadoSemTocarPedido()
    {
        var f = BuildFakes();
        var webhook = WebhookReceived();
        webhook.MarcarProcessado(EmpresaId);

        f.WebhookRepo.GetByIdAsync(webhook.Id, Arg.Any<CancellationToken>()).Returns(webhook);

        var resultado = await f.UseCase.ExecuteAsync(webhook.Id, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.JaProcessado);
        await f.MpClient.DidNotReceive().GetPaymentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.PedidoRepo.DidNotReceive().UpdateAsync(Arg.Any<Pedido>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Processar_WebhookNaoEncontrado_RetornaErro()
    {
        var f = BuildFakes();
        var idInexistente = Guid.NewGuid();
        f.WebhookRepo.GetByIdAsync(idInexistente, Arg.Any<CancellationToken>()).Returns((WebhookProcessado?)null);

        var resultado = await f.UseCase.ExecuteAsync(idInexistente, CancellationToken.None);

        resultado.Should().Be(ProcessarWebhookMpResultado.Erro);
        await f.MpClient.DidNotReceive().GetPaymentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
