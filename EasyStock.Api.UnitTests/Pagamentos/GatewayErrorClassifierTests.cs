using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Async.Pagamentos;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>Cobertura basica do <see cref="GatewayErrorClassifier"/> em P0.</summary>
public class GatewayErrorClassifierTests
{
    private readonly GatewayErrorClassifier _c = new();

    [Fact]
    public void TaskCanceled_VirarTimeout()
    {
        _c.Classify("Stripe", new TaskCanceledException()).Should().Be(ErrorCategory.Timeout);
    }

    [Fact]
    public void HttpRequestException_SemStatus_VirarNetwork()
    {
        _c.Classify("EfiPix", new HttpRequestException("connection refused")).Should().Be(ErrorCategory.Network);
    }

    [Fact]
    public void HttpRequestException_Com500_VirarServer5xx()
    {
        var ex = new HttpRequestException("server error", null, HttpStatusCode.InternalServerError);
        _c.Classify("Stripe", ex).Should().Be(ErrorCategory.Server5xx);
    }

    [Fact]
    public void StatusCode429_VirarRateLimit()
    {
        _c.Classify("MercadoPago", new InvalidOperationException("rate limited"), statusCode: 429)
            .Should().Be(ErrorCategory.RateLimit);
    }

    [Fact]
    public void StatusCode402_VirarDeclined()
    {
        _c.Classify("Stripe", new InvalidOperationException("payment required"), statusCode: 402)
            .Should().Be(ErrorCategory.Declined);
    }

    [Fact]
    public void StatusCode400_VirarInvalidData()
    {
        _c.Classify("Stripe", new InvalidOperationException("bad request"), statusCode: 400)
            .Should().Be(ErrorCategory.InvalidData);
    }

    [Fact]
    public void StatusCode401_VirarGatewayDown()
    {
        // Credenciais invalidas — fallback pra outro gateway faz sentido.
        _c.Classify("Stripe", new InvalidOperationException("unauthorized"), statusCode: 401)
            .Should().Be(ErrorCategory.GatewayDown);
    }

    [Fact]
    public void NotImplementedException_VirarInvalidData()
    {
        // Adapter stub que ainda nao implementou — bug do caller, nao retentar.
        _c.Classify("Stripe", new NotImplementedException()).Should().Be(ErrorCategory.InvalidData);
    }

    [Fact]
    public void SocketException_VirarNetwork()
    {
        _c.Classify("EfiPix", new SocketException()).Should().Be(ErrorCategory.Network);
    }

    [Fact]
    public void IOException_VirarNetwork()
    {
        _c.Classify("EfiPix", new System.IO.IOException("connection reset")).Should().Be(ErrorCategory.Network);
    }

    [Fact]
    public void SemStatusESemTipoConhecido_VirarUnknown()
    {
        _c.Classify("Stripe", new InvalidOperationException("???")).Should().Be(ErrorCategory.Unknown);
    }
}
