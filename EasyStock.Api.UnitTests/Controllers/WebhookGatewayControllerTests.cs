using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
}
