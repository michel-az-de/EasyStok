using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Pagamentos;

public class MercadoPagoSignatureValidatorTests
{
    private const string Secret = "whsec_test_mp_secret_long_enough_chars";
    private const string DataId = "pay_12345";
    private const string ReqId = "req_abcdef";

    private static MercadoPagoSignatureValidator Build(Dictionary<string, string?>? cfg = null)
    {
        cfg ??= new Dictionary<string, string?> { ["MercadoPago:WebhookSecret"] = Secret };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfg).Build();
        return new MercadoPagoSignatureValidator(configuration, NullLogger<MercadoPagoSignatureValidator>.Instance);
    }

    private static string Hmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static string Body() => $"{{\"data\":{{\"id\":\"{DataId}\"}}}}";

    [Fact]
    public void Validar_SemHeaders_RetornaFalse()
    {
        var v = Build();
        v.Validar(Body(), new Dictionary<string, string?>()).Should().BeFalse();
    }

    [Fact]
    public void Validar_HmacCorretoComTimestampAtual_RetornaTrue()
    {
        var v = Build();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var sig = Hmac(Secret, $"id:{DataId};request-id:{ReqId};ts:{ts};");
        v.Validar(Body(), new Dictionary<string, string?>
        {
            ["x-signature"] = $"ts={ts},v1={sig}",
            ["x-request-id"] = ReqId
        }).Should().BeTrue();
    }

    [Fact]
    public void Validar_TimestampForaJanela_RetornaFalse()
    {
        var v = Build();
        var ts = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString();
        var sig = Hmac(Secret, $"id:{DataId};request-id:{ReqId};ts:{ts};");
        v.Validar(Body(), new Dictionary<string, string?>
        {
            ["x-signature"] = $"ts={ts},v1={sig}",
            ["x-request-id"] = ReqId
        }).Should().BeFalse();
    }

    [Fact]
    public void Validar_TimestampEmSegundosAtual_RetornaTrue()
    {
        // Sandbox antigo MP enviava ts em segundos — heuristica deve aceitar.
        var v = Build();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = Hmac(Secret, $"id:{DataId};request-id:{ReqId};ts:{ts};");
        v.Validar(Body(), new Dictionary<string, string?>
        {
            ["x-signature"] = $"ts={ts},v1={sig}",
            ["x-request-id"] = ReqId
        }).Should().BeTrue();
    }

    [Fact]
    public void Validar_HmacIncorreto_RetornaFalse()
    {
        var v = Build();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        v.Validar(Body(), new Dictionary<string, string?>
        {
            ["x-signature"] = $"ts={ts},v1=deadbeef",
            ["x-request-id"] = ReqId
        }).Should().BeFalse();
    }

    [Fact]
    public void Validar_SemSecretComAllowUnsigned_RetornaTrue()
    {
        var v = Build(new Dictionary<string, string?> { ["MercadoPago:WebhookAllowUnsigned"] = "true" });
        v.Validar(Body(), new Dictionary<string, string?>()).Should().BeTrue();
    }

    [Fact]
    public void Validar_PayloadInvalido_RetornaFalse()
    {
        var v = Build();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        v.Validar("nao-e-json", new Dictionary<string, string?>
        {
            ["x-signature"] = $"ts={ts},v1=anything",
            ["x-request-id"] = ReqId
        }).Should().BeFalse();
    }
}
