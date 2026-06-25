using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Gera e valida tokens JWT HS256 de duracao moderada (30 dias) para
/// acompanhamento de pedido GUEST sem login (issue #680).
///
/// <para>Espelha <see cref="Avaliacao.AvaliacaoTokenService"/> com escopo
/// distinto (<c>"acomp"</c>) — segregar escopo evita que token de avaliacao
/// abra pagina de status, e vice-versa. Implementacao manual HS256, sem
/// IdentityModel.</para>
///
/// <para>Segredo lido de <c>IConfiguration["Acompanhamento:JwtSecret"]</c>
/// (≥32 chars obrigatorio).</para>
/// </summary>
public sealed class AcompanhamentoTokenService(IConfiguration configuration, TimeProvider timeProvider)
{
    private const string Scope = "acomp";
    private static readonly TimeSpan Validade = TimeSpan.FromDays(30);

    private byte[] ObterSegredo()
    {
        var secret = configuration["Acompanhamento:JwtSecret"]
            ?? throw new InvalidOperationException("Acompanhamento:JwtSecret nao configurado.");
        if (secret.Length < 32)
            throw new InvalidOperationException("Acompanhamento:JwtSecret deve ter pelo menos 32 caracteres.");
        return Encoding.UTF8.GetBytes(secret);
    }

    public string Gerar(Guid pedidoId)
    {
        var exp = (long)(timeProvider.GetUtcNow() + Validade - DateTimeOffset.UnixEpoch).TotalSeconds;

        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = pedidoId.ToString(),
            scope = Scope,
            exp,
        }));

        var signingInput = $"{header}.{payload}";
        var signature = Base64UrlEncode(ComputeHmac(signingInput));

        return $"{signingInput}.{signature}";
    }

    /// <summary>
    /// Valida o token. Lança <see cref="AcompanhamentoTokenInvalidoException"/>
    /// se assinatura, scope, sub ou exp falharem.
    /// </summary>
    public void Validar(string token, Guid expectedPedidoId)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                throw new AcompanhamentoTokenInvalidoException("JWT malformado.");

            var signingInput = $"{parts[0]}.{parts[1]}";
            var expectedSig = Base64UrlEncode(ComputeHmac(signingInput));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expectedSig),
                    Encoding.ASCII.GetBytes(parts[2])))
                throw new AcompanhamentoTokenInvalidoException("Assinatura invalida.");

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expEl) || !expEl.TryGetInt64(out var exp))
                throw new AcompanhamentoTokenInvalidoException("Campo exp ausente.");

            var now = (long)(timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;
            if (now > exp)
                throw new AcompanhamentoTokenInvalidoException("Token expirado.");

            if (!root.TryGetProperty("scope", out var scopeEl) || scopeEl.GetString() != Scope)
                throw new AcompanhamentoTokenInvalidoException("Scope invalido.");

            if (!root.TryGetProperty("sub", out var subEl)
                || !Guid.TryParse(subEl.GetString(), out var sub)
                || sub != expectedPedidoId)
                throw new AcompanhamentoTokenInvalidoException("PedidoId no token nao confere.");
        }
        catch (AcompanhamentoTokenInvalidoException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AcompanhamentoTokenInvalidoException($"Token malformado: {ex.Message}");
        }
    }

    private byte[] ComputeHmac(string input)
    {
        using var hmac = new HMACSHA256(ObterSegredo());
        return hmac.ComputeHash(Encoding.ASCII.GetBytes(input));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
