using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos.Webhooks;

/// <summary>
/// Validador de assinatura para webhooks Mercado Pago. MP usa headers
/// <c>x-signature</c> e <c>x-request-id</c>; assinatura HMAC SHA-256
/// sobre <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c> com a
/// secret <c>MercadoPago:WebhookSecret</c> obtida no painel.
///
/// <para>Formato do header <c>x-signature</c>: <c>ts=...,v1=...</c></para>
/// </summary>
public sealed class MercadoPagoSignatureValidator(
    IConfiguration configuration,
    ILogger<MercadoPagoSignatureValidator> logger) : IWebhookSignatureValidator
{
    public string Provedor => "MercadoPago";

    public bool Validar(string rawBody, IDictionary<string, string?> headers)
    {
        var secret = configuration["MercadoPago:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Equals(
                configuration["MercadoPago:WebhookAllowUnsigned"], "true",
                StringComparison.OrdinalIgnoreCase);
        }

        if (!headers.TryGetValue("x-signature", out var sigHeader) || string.IsNullOrWhiteSpace(sigHeader))
            return false;
        if (!headers.TryGetValue("x-request-id", out var reqId) || string.IsNullOrWhiteSpace(reqId))
            return false;

        string? ts = null, v1 = null;
        foreach (var p in sigHeader.Split(','))
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            var k = p[..eq].Trim();
            var v = p[(eq + 1)..].Trim();
            if (k == "ts") ts = v;
            else if (k == "v1") v1 = v;
        }
        if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(v1)) return false;

        // Extrai data.id do payload — campo obrigatorio na manifest MP.
        string? dataId = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            if (doc.RootElement.TryGetProperty("data", out var dataEl)
                && dataEl.TryGetProperty("id", out var idEl))
            {
                dataId = idEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? idEl.GetString()
                    : idEl.ToString();
            }
        }
        catch
        {
            logger.LogWarning("MercadoPago webhook: payload invalido (nao parseou JSON).");
            return false;
        }
        if (string.IsNullOrWhiteSpace(dataId)) return false;

        var toSign = $"id:{dataId};request-id:{reqId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(v1.Trim().ToLowerInvariant()));
    }
}
