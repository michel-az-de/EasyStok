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
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

public class WebhookWhatsAppControllerTests
{
    private const string VerifyToken = "verify-teste";
    private const string AppSecret = "app-secret-teste";

    private readonly IWebhookRecebidoRepository _webhookRecebidoRepository = Substitute.For<IWebhookRecebidoRepository>();
    private readonly IEmpresaRepository _empresaRepository = Substitute.For<IEmpresaRepository>();
    private readonly ITenantFeatureFlagRepository _featureFlagRepository = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly WebhookWhatsAppController _controller;

    public WebhookWhatsAppControllerTests()
    {
        var processarUseCase = new ProcessarEventoWhatsAppUseCase(
            _empresaRepository,
            _featureFlagRepository,
            Substitute.For<IConfiguracaoAtendimentoRepository>(),
            Substitute.For<IConversaRepository>(),
            _webhookRecebidoRepository,
            Substitute.For<IWhatsAppCloudClient>(),
            Substitute.For<IQueueService>(),
            Substitute.For<IOperacaoEventPublisher>(),
            Substitute.For<ITenantContextAccessor>(),
            _unitOfWork,
            new IdentificarClientePorTelefoneUseCase(
                Substitute.For<IClienteRepository>(),
                Substitute.For<IClienteStorefrontRepository>(),
                NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), new ConfigurationBuilder().Build()),
            NullLogger<ProcessarEventoWhatsAppUseCase>.Instance);

        var metaOptions = Options.Create(new MetaCloudWhatsAppOptions { VerifyToken = VerifyToken, AppSecret = AppSecret });

        _controller = new WebhookWhatsAppController(processarUseCase, metaOptions, NullLogger<WebhookWhatsAppController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public void VerificacaoDevolveChallenge()
    {
        var result = _controller.Verificar("subscribe", VerifyToken, "challenge-123");

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("challenge-123");
    }

    [Fact]
    public void TokenErrado403()
    {
        var result = _controller.Verificar("subscribe", "token-errado", "challenge-123");

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AssinaturaInvalida403()
    {
        SetRequestBody("{}", assinaturaValida: false);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await _webhookRecebidoRepository.DidNotReceiveWithAnyArgs()
            .TryRegistrarAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AssinaturaValidaDevolve200()
    {
        SetRequestBody("{}", assinaturaValida: true);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task FalhaQueUmReenvioResolveDevolve503()
    {
        // 503 faz a Meta reenviar; 200 perderia a mensagem (ex.: corrida na 1ª mensagem do contato).
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        _empresaRepository.GetByWhatsAppPhoneNumberIdAsync("PHONE123", Arg.Any<CancellationToken>()).Returns(empresa);
        _featureFlagRepository.ListarAtivasAsync(empresa.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _unitOfWork.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("23505"));
        const string payload = "{\"entry\":[{\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"PHONE123\"},\"messages\":[{\"from\":\"5511999998888\",\"id\":\"wamid.c\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\"oi\"}}]}}]}]}";
        SetRequestBody(payload, assinaturaValida: true);

        var result = await _controller.Receber(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private void SetRequestBody(string body, bool assinaturaValida)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var stream = new MemoryStream(bytes);
        _controller.ControllerContext.HttpContext.Request.Body = stream;
        _controller.ControllerContext.HttpContext.Request.ContentLength = bytes.Length;

        var assinatura = assinaturaValida
            ? "sha256=" + ComputeHmac(body, AppSecret)
            : "sha256=" + new string('0', 64);

        _controller.ControllerContext.HttpContext.Request.Headers["X-Hub-Signature-256"] = assinatura;
    }

    private static string ComputeHmac(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
