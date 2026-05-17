using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Pagamentos;

public class StripeSignatureValidatorTests
{
    private const string Secret = "whsec_test_stripe_secret_long_enough_chars";

    private static StripeSignatureValidator Build(Dictionary<string, string?>? cfg = null)
    {
        cfg ??= new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfg).Build();
        return new StripeSignatureValidator(configuration, NullLogger<StripeSignatureValidator>.Instance);
    }

    private static string Hmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    [Fact]
    public void Validar_SemHeader_RetornaFalse()
    {
        var v = Build();
        v.Validar("{}", new Dictionary<string, string?>()).Should().BeFalse();
    }

    [Fact]
    public void Validar_HmacCorretoComTimestamp_RetornaTrue()
    {
        var v = Build();
        var body = "{\"id\":\"evt_test\"}";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = Hmac(Secret, $"{ts}.{body}");
        var ok = v.Validar(body, new Dictionary<string, string?>
        {
            ["Stripe-Signature"] = $"t={ts},v1={sig}"
        });
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validar_HmacIncorreto_RetornaFalse()
    {
        var v = Build();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var ok = v.Validar("{}", new Dictionary<string, string?>
        {
            ["Stripe-Signature"] = $"t={ts},v1=deadbeef"
        });
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validar_TimestampForaJanela_RetornaFalse()
    {
        var v = Build();
        var body = "{}";
        var tsAntigo = (DateTimeOffset.UtcNow.AddMinutes(-10)).ToUnixTimeSeconds().ToString();
        var sig = Hmac(Secret, $"{tsAntigo}.{body}");
        var ok = v.Validar(body, new Dictionary<string, string?>
        {
            ["Stripe-Signature"] = $"t={tsAntigo},v1={sig}"
        });
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validar_MultiplasV1_AceitaSeUmaBate()
    {
        // Stripe envia v1 multiplas durante rotacao de secret. Aceita se UMA bate.
        var v = Build();
        var body = "{}";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = Hmac(Secret, $"{ts}.{body}");
        var ok = v.Validar(body, new Dictionary<string, string?>
        {
            ["Stripe-Signature"] = $"t={ts},v1=oldsigwrong,v1={sig}"
        });
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validar_SemSecretComAllowUnsigned_RetornaTrue()
    {
        var v = Build(new Dictionary<string, string?> { ["Stripe:WebhookAllowUnsigned"] = "true" });
        v.Validar("{}", new Dictionary<string, string?>()).Should().BeTrue();
    }
}
