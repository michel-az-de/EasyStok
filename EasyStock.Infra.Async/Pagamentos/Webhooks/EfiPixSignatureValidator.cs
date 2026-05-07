using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Validador HMAC SHA-256 para webhooks Pix da Efi. Identica ao bloco
/// <c>ValidarAssinatura</c> do <c>WebhookPixController</c> original — extraido
/// para permitir reuso pelo <c>WebhookGatewayController</c> generico.
///
/// <para>
/// Configuracao:
/// </para>
/// <list type="bullet">
///   <item><c>Efi:WebhookSecret</c> — segredo HMAC. Sem isso, valida apenas se
///   <c>Efi:WebhookAllowUnsigned == "true"</c> (uso em DEV/sandbox).</item>
/// </list>
/// <para>
/// Headers esperados:
/// </para>
/// <list type="bullet">
///   <item><c>X-Efi-Signature</c> (hex lowercase) — obrigatorio quando ha secret.</item>
///   <item><c>X-Efi-Timestamp</c> (unix ms) — opcional. Quando presente,
///   janela maxima ±5min para protecao anti-replay; e incluido no payload assinado
///   como <c>{ts}.{body}</c>.</item>
/// </list>
/// </summary>
public sealed class EfiPixSignatureValidator(
    IConfiguration configuration,
    ILogger<EfiPixSignatureValidator> logger) : IWebhookSignatureValidator
{
    public string Provedor => "EfiPix";

    public bool Validar(string rawBody, IDictionary<string, string?> headers)
    {
        var secret = configuration["Efi:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Equals(
                configuration["Efi:WebhookAllowUnsigned"],
                "true",
                StringComparison.OrdinalIgnoreCase);
        }

        if (!headers.TryGetValue("X-Efi-Signature", out var headerSig)
            || string.IsNullOrWhiteSpace(headerSig))
            return false;

        // Replay protection: header X-Efi-Timestamp em ms unix; aceita janela ±5min.
        headers.TryGetValue("X-Efi-Timestamp", out var tsHeader);
        if (long.TryParse(tsHeader, out var ts))
        {
            var diff = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts);
            if (diff > 5 * 60 * 1000)
            {
                logger.LogWarning("Webhook Pix: timestamp fora da janela ({Diff}ms). Recusando.", diff);
                return false;
            }
        }

        // Inclui timestamp no payload assinado se presente (anti-replay).
        var toSign = string.IsNullOrWhiteSpace(tsHeader) ? rawBody : $"{tsHeader}.{rawBody}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(headerSig.Trim().ToLowerInvariant()));
    }
}
