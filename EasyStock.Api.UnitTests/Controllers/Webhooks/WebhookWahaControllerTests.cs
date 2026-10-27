using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Controllers.Webhooks;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

/// <summary>
/// #2072: o webhook do WAHA só existe com a ponte ligada, confere o HMAC sha512 do corpo bruto e entrega ao mesmo use
/// case da Meta, roteado pelo número da loja.
/// </summary>
public class WebhookWahaControllerTests
{
    private const string Segredo = "segredo-hmac-de-teste-com-mais-de-32-caracteres";
    private const string Loja = "5541999990000";

    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IWebhookRecebidoRepository _recebidos = Substitute.For<IWebhookRecebidoRepository>();

    private WebhookWahaController Controller(bool ligada = true)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var processar = new ProcessarEventoWhatsAppUseCase(
            _empresas,
            Substitute.For<ITenantFeatureFlagRepository>(),
            Substitute.For<IConfiguracaoAtendimentoRepository>(),
            Substitute.For<IConversaRepository>(),
            _recebidos,
            Substitute.For<IWhatsAppCloudClient>(),
            Substitute.For<IQueueService>(),
            Substitute.For<IOperacaoEventPublisher>(),
            Substitute.For<ITenantContextAccessor>(),
            unitOfWork,
            new IdentificarClientePorTelefoneUseCase(
                Substitute.For<IClienteRepository>(),
                Substitute.For<IClienteStorefrontRepository>(),
                NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), new ConfigurationBuilder().Build()),
            new RoteadorAcoesBotao([], NullLogger<RoteadorAcoesBotao>.Instance),
            new OptOutPorPalavra(Substitute.For<IConsentimentoContatoRepository>(), Substitute.For<IConversaRepository>(),
                new ResolvedorCanal([]), unitOfWork, NullLogger<OptOutPorPalavra>.Instance),
            Substitute.For<IEscaladorConversa>(),
            Substitute.For<IExpedienteLojaRepository>(),
            TimeProvider.System,
            NullLogger<ProcessarEventoWhatsAppUseCase>.Instance);

        var opcoes = Options.Create(new WahaOptions
        {
            Ligada = ligada, HmacKey = Segredo, PhoneNumberId = Loja, Sessao = "default", BaseUrl = "http://waha.test:3000",
        });
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var http = new HttpClient { BaseAddress = new Uri("http://waha.test:3000/") };
        var tradutor = new TradutorEventoWaha(
            new ResolvedorContatoWaha(http, opcoes, cache, NullLogger<ResolvedorContatoWaha>.Instance),
            new MenuNumeradoWaha(cache, opcoes, NullLogger<MenuNumeradoWaha>.Instance),
            opcoes, NullLogger<TradutorEventoWaha>.Instance);

        return new WebhookWahaController(processar, tradutor, opcoes, NullLogger<WebhookWahaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static string Mensagem(string evento = "message.any") =>
        $$$"""{"id":"evt_1","timestamp":1730000000000,"event":"{{{evento}}}","session":"default","me":{"id":"{{{Loja}}}@c.us","pushName":"Casa da Babá"},"payload":{"id":"false_5541988887777@c.us_3EB0C0FFEE","timestamp":1730000000,"from":"5541988887777@c.us","fromMe":false,"source":"app","body":"Olá, quero um bolo","hasMedia":false,"status":"WORKING"}}""";

    private static string Assinar(byte[] corpo, string segredo = Segredo) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(segredo), corpo)).ToLowerInvariant();

    private static void Corpo(ControllerBase controller, string json, string? assinatura)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        controller.HttpContext.Request.Body = new MemoryStream(bytes);
        if (assinatura is not null) controller.HttpContext.Request.Headers[WebhookWahaController.HeaderAssinatura] = assinatura;
    }

    [Fact]
    public async Task PonteDesligadaResponde404()
    {
        var controller = Controller(ligada: false);
        var json = Mensagem();
        Corpo(controller, json, Assinar(Encoding.UTF8.GetBytes(json)));

        (await controller.Receber(CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        await _empresas.DidNotReceiveWithAnyArgs().GetByWhatsAppPhoneNumberIdAsync(default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-hex")]
    [InlineData("00")]
    public async Task AssinaturaInvalidaResponde403(string? assinatura)
    {
        var controller = Controller();
        Corpo(controller, Mensagem(), assinatura);

        (await controller.Receber(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        await _empresas.DidNotReceiveWithAnyArgs().GetByWhatsAppPhoneNumberIdAsync(default!, default);
    }

    [Fact]
    public async Task AssinaturaComOutroSegredoResponde403()
    {
        var controller = Controller();
        var json = Mensagem();
        Corpo(controller, json, Assinar(Encoding.UTF8.GetBytes(json), "outro-segredo-qualquer-com-mais-de-32-chars"));

        (await controller.Receber(CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task MensagemAssinadaChegaAoAtendimentoPeloNumeroDaLoja()
    {
        var controller = Controller();
        var json = Mensagem();
        Corpo(controller, json, Assinar(Encoding.UTF8.GetBytes(json)).ToUpperInvariant());

        (await controller.Receber(CancellationToken.None)).Should().BeOfType<OkResult>();
        await _empresas.Received(1).GetByWhatsAppPhoneNumberIdAsync(Loja, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EventoQueNaoEhMensagemResponde200SemIrAoAtendimento()
    {
        var controller = Controller();
        var json = Mensagem("session.status");
        Corpo(controller, json, Assinar(Encoding.UTF8.GetBytes(json)));

        (await controller.Receber(CancellationToken.None)).Should().BeOfType<OkResult>();
        await _empresas.DidNotReceiveWithAnyArgs().GetByWhatsAppPhoneNumberIdAsync(default!, default);
    }

    [Fact]
    public void SemSegredoNadaPassa() =>
        WebhookWahaController.AssinaturaValida(Assinar([1, 2, 3]), [1, 2, 3], segredo: "").Should().BeFalse();
}
