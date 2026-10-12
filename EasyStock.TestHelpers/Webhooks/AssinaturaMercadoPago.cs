using System.Security.Cryptography;
using System.Text;

namespace EasyStock.TestHelpers.Webhooks;

/// <summary>
/// Gera os cabeçalhos de um webhook do Mercado Pago como o provedor assina (#1634), para o teste passar pelo
/// validador real em vez de desligá-lo. Manifest: <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>,
/// HMAC SHA-256 com o segredo do webhook, hexadecimal minúsculo; <c>ts</c> em milissegundos Unix.
///
/// <para>
/// O formato é o que <c>EasyStock.Infra.Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs</c> valida.
/// O <c>instante</c> existe para testar a janela de 5 minutos contra replay.
/// </para>
/// </summary>
public static class AssinaturaMercadoPago
{
    public const string HeaderAssinatura = "x-signature";
    public const string HeaderRequestId = "x-request-id";

    /// <summary>Valor do <c>x-signature</c>: <c>ts=...,v1=...</c>.</summary>
    public static string Gerar(string segredo, string dataId, string requestId, DateTimeOffset instante)
    {
        var ts = instante.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var manifest = $"id:{dataId};request-id:{requestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(segredo));
        var v1 = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return $"ts={ts},v1={v1}";
    }

    /// <summary>Os dois cabeçalhos (<c>x-signature</c> e <c>x-request-id</c>) no formato que o validador recebe.</summary>
    public static Dictionary<string, string?> Cabecalhos(
        string segredo, string dataId, string requestId, DateTimeOffset instante) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderAssinatura] = Gerar(segredo, dataId, requestId, instante),
            [HeaderRequestId] = requestId,
        };
}
