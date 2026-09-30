using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Api.UnitTests.Pagamentos;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #787: quando o dedup de webhook bate mas a tentativa anterior NAO teve sucesso,
/// o controller deve REPROCESSAR (não responder 200 e perder o pagamento no retry).
/// </summary>
public class WebhookGatewayControllerTests
{
    private const string Provedor = "efi";

    private readonly IWebhookRecebidoRepository _repo = Substitute.For<IWebhookRecebidoRepository>();
    private readonly IGatewayWebhookProcessor _processor = Substitute.For<IGatewayWebhookProcessor>();
    private readonly IWebhookSignatureValidator _validator = Substitute.For<IWebhookSignatureValidator>();

    private WebhookGatewayController CriarController()
    {
        _validator.Provedor.Returns(Provedor);
        _validator.Validar(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>>()).Returns(true);
        _processor.Provedor.Returns(Provedor);

        var controller = new WebhookGatewayController(
            new[] { _processor }, new[] { _validator }, _repo,
            Substitute.For<ILogger<WebhookGatewayController>>());

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"evento\":\"pix\"}"));
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    [Fact]
    public async Task Dedup_ComTentativaAnteriorSemSucesso_Reprocessa()
    {
        // TryRegistrar retorna null (ja existe) e o existente falhou antes.
        _repo.TryRegistrarAsync(Provedor, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebhookRecebido?)null);
        var anterior = WebhookRecebido.Criar(Provedor, "evt-1", "hash");
        anterior.MarcarProcessado(sucesso: false, erro: "deadlock");
        _repo.ObterAsync(Provedor, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(anterior);

        var result = await CriarController().Receber(Provedor);

        result.Should().BeOfType<OkResult>();
        await _processor.Received(1).ProcessarAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).MarcarProcessadoAsync(anterior.Id, true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dedup_ComTentativaAnteriorComSucesso_NaoReprocessa()
    {
        _repo.TryRegistrarAsync(Provedor, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebhookRecebido?)null);
        var anterior = WebhookRecebido.Criar(Provedor, "evt-1", "hash");
        anterior.MarcarProcessado(sucesso: true);
        _repo.ObterAsync(Provedor, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(anterior);

        var result = await CriarController().Receber(Provedor);

        result.Should().BeOfType<OkResult>();
        await _processor.DidNotReceive().ProcessarAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>>(), Arg.Any<CancellationToken>());
    }

    // S32: /api/webhooks/mercadopago respondia 500 porque não havia processor registrado.
    private const string SegredoMp = "segredo-de-teste";

    private static WebhookGatewayController ControllerMercadoPago(
        MercadoPagoWebhookFixture f, IWebhookRecebidoRepository repo, string corpo, string? assinatura = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MercadoPago:WebhookSecret"] = SegredoMp,
        }).Build();
        var validator = new MercadoPagoSignatureValidator(config, NullLogger<MercadoPagoSignatureValidator>.Instance);
        var controller = new WebhookGatewayController(
            new IGatewayWebhookProcessor[] { f.Processor() }, new IWebhookSignatureValidator[] { validator }, repo,
            NullLogger<WebhookGatewayController>.Instance);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string requestId = "req-1";
        var manifesto = $"id:{MercadoPagoWebhookFixture.PagamentoId};request-id:{requestId};ts:{ts};";
        var v1 = assinatura ?? Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(SegredoMp), Encoding.UTF8.GetBytes(manifesto))).ToLowerInvariant();

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(corpo));
        ctx.Request.Headers["x-signature"] = $"ts={ts},v1={v1}";
        ctx.Request.Headers["x-request-id"] = requestId;
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    private static IWebhookRecebidoRepository RepoQueRegistra()
    {
        var repo = Substitute.For<IWebhookRecebidoRepository>();
        repo.TryRegistrarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => WebhookRecebido.Criar(ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<string>(2)));
        return repo;
    }

    [Fact]
    public async Task MercadoPagoComProcessorRetorna200()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte("approved", 25m);
        var repo = RepoQueRegistra();

        var result = await ControllerMercadoPago(f, repo, MercadoPagoWebhookFixture.Notificacao()).Receber("mercadopago");

        result.Should().BeOfType<OkResult>();
        await repo.Received(1).TryRegistrarAsync("MercadoPago", "MercadoPago:123456789", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repo.Received(1).MarcarProcessadoAsync(Arg.Any<Guid>(), true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
    }

    [Fact]
    public async Task MercadoPagoAssinaturaInvalidaRetorna401ENadaGrava()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte("approved", 25m);
        var repo = RepoQueRegistra();

        var result = await ControllerMercadoPago(f, repo, MercadoPagoWebhookFixture.Notificacao(), assinatura: new string('0', 64))
            .Receber("mercadopago");

        result.Should().BeOfType<UnauthorizedResult>();
        await repo.DidNotReceiveWithAnyArgs().TryRegistrarAsync(default!, default!, default!, default);
        await f.MpClient.DidNotReceiveWithAnyArgs().ConsultarPagamentoAsync(default!, default);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }
}
