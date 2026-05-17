using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Validador de assinatura para webhooks Stripe. Stripe usa header
/// <c>Stripe-Signature</c> no formato <c>t={timestamp},v1={hmac_hex}</c>.
/// O HMAC SHA-256 e calculado sobre <c>{timestamp}.{rawBody}</c> com a
/// secret <c>Stripe:WebhookSecret</c>.
///
/// <para>
/// Sem secret configurado, recusa por padrao (a menos que
/// <c>Stripe:WebhookAllowUnsigned=true</c> em DEV/sandbox).
/// </para>
/// </summary>
public sealed class StripeSignatureValidator(
    IConfiguration configuration,
    ILogger<StripeSignatureValidator> logger) : IWebhookSignatureValidator
{
    public string Provedor => "Stripe";

    public bool Validar(string rawBody, IDictionary<string, string?> headers)
    {
        var secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Equals(
                configuration["Stripe:WebhookAllowUnsigned"], "true",
                StringComparison.OrdinalIgnoreCase);
        }

        if (!headers.TryGetValue("Stripe-Signature", out var sigHeader)
            || string.IsNullOrWhiteSpace(sigHeader))
            return false;

        // Stripe-Signature: t=1492774577,v1=5257a869e7...
        var parts = sigHeader.Split(',');
        string? ts = null;
        var v1 = new List<string>();
        foreach (var p in parts)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            var k = p[..eq].Trim();
            var v = p[(eq + 1)..].Trim();
            if (k == "t") ts = v;
            else if (k == "v1") v1.Add(v);
        }

        if (string.IsNullOrWhiteSpace(ts) || v1.Count == 0) return false;

        // Replay protection: ±5min
        if (long.TryParse(ts, out var tsUnix))
        {
            var diff = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - tsUnix);
            if (diff > 5 * 60)
            {
                logger.LogWarning("Stripe webhook: timestamp fora da janela ({Diff}s).", diff);
                return false;
            }
        }

        var toSign = $"{ts}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();

        // Stripe pode enviar varias v1 (rotacao de secret). Aceita se UMA delas bate.
        return v1.Any(v => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(v.Trim().ToLowerInvariant())));
    }
}
