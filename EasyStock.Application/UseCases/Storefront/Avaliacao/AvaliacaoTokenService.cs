using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Gera e valida tokens JWT HS256 de curta duração (30 dias) para abertura segura
/// da página de avaliação via link WhatsApp (TASK-EZ-AVAL-001).
///
/// <para>
/// Implementação manual HS256 — sem dependência de System.IdentityModel.Tokens.Jwt
/// na camada Application. Payload mínimo: { sub, scope, exp }.
/// </para>
///
/// <para>
/// Segredo lido de <c>IConfiguration["Avaliacao:JwtSecret"]</c> (≥32 chars obrigatório).
/// </para>
/// </summary>
public sealed class AvaliacaoTokenService(IConfiguration configuration, TimeProvider timeProvider)
{
    private const string Scope = "aval";
    private static readonly TimeSpan Validade = TimeSpan.FromDays(30);

    private byte[] ObterSegredo()
    {
        var secret = configuration["Avaliacao:JwtSecret"]
            ?? throw new InvalidOperationException("Avaliacao:JwtSecret não configurado.");
        if (secret.Length < 32)
            throw new InvalidOperationException("Avaliacao:JwtSecret deve ter pelo menos 32 caracteres.");
        return Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// Gera token JWT HS256 com <c>sub = pedidoId</c>, <c>scope = "aval"</c>,
    /// expirando em 30 dias a partir de agora.
    /// </summary>
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
    /// Valida o token. Lança <see cref="AvaliacaoTokenInvalidoException"/> se:
    /// <list type="bullet">
    ///   <item>Assinatura inválida</item>
    ///   <item>Token expirado</item>
    ///   <item>scope != "aval"</item>
    ///   <item>sub != <paramref name="expectedPedidoId"/></item>
    ///   <item>Token malformado</item>
    /// </list>
    /// </summary>
    public void Validar(string token, Guid expectedPedidoId)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                throw new AvaliacaoTokenInvalidoException("JWT malformado.");

            var signingInput = $"{parts[0]}.{parts[1]}";
            var expectedSig = Base64UrlEncode(ComputeHmac(signingInput));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expectedSig),
                    Encoding.ASCII.GetBytes(parts[2])))
                throw new AvaliacaoTokenInvalidoException("Assinatura inválida.");

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expEl) || !expEl.TryGetInt64(out var exp))
                throw new AvaliacaoTokenInvalidoException("Campo exp ausente.");

            var now = (long)(timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;
            if (now > exp)
                throw new AvaliacaoTokenInvalidoException("Token expirado.");

            if (!root.TryGetProperty("scope", out var scopeEl) || scopeEl.GetString() != Scope)
                throw new AvaliacaoTokenInvalidoException("Scope inválido.");

            if (!root.TryGetProperty("sub", out var subEl)
                || !Guid.TryParse(subEl.GetString(), out var sub)
                || sub != expectedPedidoId)
                throw new AvaliacaoTokenInvalidoException("PedidoId no token não confere.");
        }
        catch (AvaliacaoTokenInvalidoException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AvaliacaoTokenInvalidoException($"Token malformado: {ex.Message}");
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
