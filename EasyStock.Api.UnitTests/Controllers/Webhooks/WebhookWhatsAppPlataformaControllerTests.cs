using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Controllers.Webhooks;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

/// <summary>N6: webhook do número de plataforma, com verify token e rota próprios, e o mesmo HMAC do app.</summary>
public class WebhookWhatsAppPlataformaControllerTests
{
    private const string VerifyTokenPlataforma = "verify-plataforma";
    private const string VerifyTokenAtendimento = "verify-atendimento";
    private const string AppSecret = "app-secret-teste";
    private const string NumeroPlataforma = "7770009999";

    private readonly IOutboxNotificacaoRepository _outbox = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly WebhookWhatsAppPlataformaController _controller;

    public WebhookWhatsAppPlataformaControllerTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = NumeroPlataforma
        }).Build();
        var status = new ProcessarStatusWhatsAppPlataformaUseCase(
            _outbox, Substitute.For<ITenantContextAccessor>(), _uow, config,
            NullLogger<ProcessarStatusWhatsAppPlataformaUseCase>.Instance);
        var responder = new ResponderMensagemRecebidaPlataformaUseCase(
            Substitute.For<IClienteWhatsAppPlataforma>(), Substitute.For<ICacheService>(),
            NullLogger<ResponderMensagemRecebidaPlataformaUseCase>.Instance);
        var processar = new ProcessarWebhookWhatsAppPlataformaUseCase(
            status, responder, config, NullLogger<ProcessarWebhookWhatsAppPlataformaUseCase>.Instance);

        _controller = new WebhookWhatsAppPlataformaController(
            processar,
            Options.Create(new MetaCloudWhatsAppOptions { AppSecret = AppSecret, VerifyToken = VerifyTokenAtendimento }),
            Options.Create(new WhatsAppPlataformaOptions { VerifyToken = VerifyTokenPlataforma, PhoneNumberId = NumeroPlataforma }),
            NullLogger<WebhookWhatsAppPlataformaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public void VerificacaoDevolveChallengeComOTokenDaPlataforma()
    {
        var result = _controller.Verificar("subscribe", VerifyTokenPlataforma, "challenge-123");

        result.Should().BeOfType<ContentResult>().Which.Content.Should().Be("challenge-123");
    }

    [Fact]
    public void TokenDoAtendimentoNaoVerificaAPlataforma()
    {
        var result = _controller.Verificar("subscribe", VerifyTokenAtendimento, "challenge-123");

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AssinaturaInvalida403()
    {
        SetRequestBody("{}", assinaturaValida: false);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AssinaturaValidaDevolve200()
    {
        SetRequestBody("{}", assinaturaValida: true);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task FalhaTransitoriaDevolve503()
    {
        var empresaId = Guid.NewGuid();
        var mensagem = OutboxMensagemNotificacao.Criar(
            Guid.NewGuid(), Guid.NewGuid(), empresaId, CanalNotificacao.WhatsApp, "+5511999990001", "", "c",
            CategoriaConteudoNotificacao.Operacional, remetente: OrigemRemetente.Plataforma);
        mensagem.MarcarIndeterminado("timeout", "meta-plataforma");
        _outbox.ObterAsync(empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        _uow.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("db"));
        var payload = """{"entry":[{"changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"NUM"},"statuses":[{"id":"wamid.S","status":"sent","biz_opaque_callback_data":"OPACO"}]}}]}]}"""
            .Replace("NUM", NumeroPlataforma).Replace("OPACO", $"{empresaId:N}.{mensagem.Id:N}");
        SetRequestBody(payload, assinaturaValida: true);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void UsaRateLimitDaMeta()
    {
        var atributo = typeof(WebhookWhatsAppPlataformaController)
            .GetMethod(nameof(WebhookWhatsAppPlataformaController.Receber))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        atributo.PolicyName.Should().Be("webhook-meta");
        typeof(WebhookWhatsAppPlataformaController).GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>().Single().Template.Should().Be("api/webhooks/whatsapp-plataforma");
    }

    private void SetRequestBody(string body, bool assinaturaValida)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        _controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(bytes);
        _controller.ControllerContext.HttpContext.Request.ContentLength = bytes.Length;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var assinatura = assinaturaValida
            ? "sha256=" + Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant()
            : "sha256=" + new string('0', 64);
        _controller.ControllerContext.HttpContext.Request.Headers["X-Hub-Signature-256"] = assinatura;
    }
}
