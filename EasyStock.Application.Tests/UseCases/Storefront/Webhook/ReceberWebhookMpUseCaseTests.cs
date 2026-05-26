using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Webhook;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases.Storefront.Webhook;

/// <summary>
/// Tests do <see cref="ReceberWebhookMpUseCase"/> — endpoint síncrono de webhook
/// (ADR-0006 §Receive): HMAC + INSERT atômico + dedup.
/// </summary>
public class ReceberWebhookMpUseCaseTests
{
    private const string Secret = "test-webhook-secret-mercadopago-easystok";
    private const string XRequestId = "req-12345-abc";

    private static byte[] PayloadValido(string paymentId = "987654321", string tipo = "payment.updated")
        => Encoding.UTF8.GetBytes(
            $"{{\"id\":111,\"action\":\"{tipo}\",\"type\":\"payment\",\"data\":{{\"id\":\"{paymentId}\"}}}}");

    private static string ComputarHmac(string secret, byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private sealed record Fakes(
        IWebhookProcessadoRepository WebhookRepo,
        IMpWebhookSecretProvider SecretProvider,
        ReceberWebhookMpUseCase UseCase);

    private static Fakes BuildFakes(bool insercaoBemSucedida = true)
    {
        var webhookRepo = Substitute.For<IWebhookProcessadoRepository>();
        webhookRepo.TentarRegistrarRecebidoAsync(Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var arg = callInfo.Arg<WebhookProcessado>();
                return Task.FromResult<(bool inserido, WebhookProcessado registro)>(
                    (insercaoBemSucedida, arg));
            });

        var secretProvider = Substitute.For<IMpWebhookSecretProvider>();
        secretProvider.ObterSecret().Returns(Secret);

        var useCase = new ReceberWebhookMpUseCase(
            webhookRepo,
            secretProvider,
            new MpHmacValidator(),
            NullLogger<ReceberWebhookMpUseCase>.Instance);

        return new Fakes(webhookRepo, secretProvider, useCase);
    }

    [Fact]
    public async Task Receber_HmacValidoENovoEvento_RetornaAceito_EInserePersistencia()
    {
        var f = BuildFakes(insercaoBemSucedida: true);
        var payload = PayloadValido("payment-aprovado-001");
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.Aceito);
        await f.WebhookRepo.Received(1).TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());

        var (chamada, _) = f.WebhookRepo.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IWebhookProcessadoRepository.TentarRegistrarRecebidoAsync))
            .GetArguments() switch
        { var args => (args[0] as WebhookProcessado, args[1]) };

        chamada.Should().NotBeNull();
        chamada!.Provider.Should().Be("mercadopago");
        chamada.EventoId.Should().Be(XRequestId);
        chamada.Tipo.Should().Be("payment.updated");
        chamada.Status.Should().Be(WebhookProcessadoStatus.Received);
    }

    [Fact]
    public async Task Receber_HmacValidoEEventoDuplicado_RetornaDuplicado_SemReprocessar()
    {
        var f = BuildFakes(insercaoBemSucedida: false);
        var payload = PayloadValido("payment-aprovado-002");
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.Duplicado);
        await f.WebhookRepo.Received(1).TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_HmacInvalido_RetornaHmacInvalido_SemPersistir()
    {
        var f = BuildFakes();
        var payload = PayloadValido();
        const string assinaturaErrada = "0000000000000000000000000000000000000000000000000000000000000000";

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinaturaErrada, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.HmacInvalido);
        await f.WebhookRepo.DidNotReceive().TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_AssinaturaAusente_RetornaHmacInvalido()
    {
        var f = BuildFakes();
        var payload = PayloadValido();

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, string.Empty, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.HmacInvalido);
        await f.WebhookRepo.DidNotReceive().TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_PayloadVazio_RetornaPayloadInvalido_SemValidarHmac()
    {
        var f = BuildFakes();

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(Array.Empty<byte>(), "nao-importa", XRequestId),
            CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.PayloadInvalido);
        await f.WebhookRepo.DidNotReceive().TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_PayloadMalformado_RetornaPayloadInvalido()
    {
        var f = BuildFakes();
        var payload = Encoding.UTF8.GetBytes("isto-nao-eh-json");
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.PayloadInvalido);
    }

    [Fact]
    public async Task Receber_SemXRequestId_UsaDataIdComoEventoId()
    {
        var f = BuildFakes();
        var payload = PayloadValido("payment-aprovado-007", "payment.created");
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, string.Empty), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.Aceito);
        await f.WebhookRepo.Received(1).TentarRegistrarRecebidoAsync(
            Arg.Is<WebhookProcessado>(w => w.EventoId == "payment-aprovado-007"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_PayloadSemDataId_E_SemXRequestId_RetornaPayloadInvalido()
    {
        var f = BuildFakes();
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"payment\",\"action\":\"payment.updated\"}");
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, string.Empty), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.PayloadInvalido);
        await f.WebhookRepo.DidNotReceive().TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_SecretProviderLanca_RetornaHmacInvalido()
    {
        var f = BuildFakes();
        f.SecretProvider.ObterSecret().Returns(_ => throw new InvalidOperationException("secret missing"));
        var payload = PayloadValido();
        var assinatura = ComputarHmac(Secret, payload);

        var resultado = await f.UseCase.ExecuteAsync(
            new ReceberWebhookMpInput(payload, assinatura, XRequestId), CancellationToken.None);

        resultado.Should().Be(ReceberWebhookMpResultado.HmacInvalido);
        await f.WebhookRepo.DidNotReceive().TentarRegistrarRecebidoAsync(
            Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receber_Idempotente_DuasChamadasConsecutivas_NaoPersistemDuasVezes()
    {
        var webhookRepo = Substitute.For<IWebhookProcessadoRepository>();
        var calls = 0;
        webhookRepo.TentarRegistrarRecebidoAsync(Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                calls++;
                var arg = callInfo.Arg<WebhookProcessado>();
                return Task.FromResult<(bool inserido, WebhookProcessado registro)>(
                    (calls == 1, arg));
            });

        var secretProvider = Substitute.For<IMpWebhookSecretProvider>();
        secretProvider.ObterSecret().Returns(Secret);

        var useCase = new ReceberWebhookMpUseCase(
            webhookRepo, secretProvider, new MpHmacValidator(),
            NullLogger<ReceberWebhookMpUseCase>.Instance);

        var payload = PayloadValido("idempotency-test");
        var assinatura = ComputarHmac(Secret, payload);
        var input = new ReceberWebhookMpInput(payload, assinatura, XRequestId);

        var primeira = await useCase.ExecuteAsync(input, CancellationToken.None);
        var segunda = await useCase.ExecuteAsync(input, CancellationToken.None);

        primeira.Should().Be(ReceberWebhookMpResultado.Aceito);
        segunda.Should().Be(ReceberWebhookMpResultado.Duplicado);
        calls.Should().Be(2);
    }
}
