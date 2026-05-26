using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.UseCases.Storefront.Webhook;
using FluentAssertions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Webhook;

/// <summary>
/// Tests do <see cref="MpHmacValidator"/> — validação HMAC-SHA256 do payload
/// MP usando <c>WebhookSecret</c> por tenant. Padrão constant-time (FixedTimeEquals).
/// </summary>
public class MpHmacValidatorTests
{
    private const string Secret = "test-secret-da-cred-credencial-integracao";

    private static string ComputeHmac(string secret, byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Validar_HmacCorreto_RetornaTrue()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\",\"action\":\"payment.updated\"}");
        var assinaturaEsperada = ComputeHmac(Secret, payload);

        validator.EhValido(payload, assinaturaEsperada, Secret).Should().BeTrue();
    }

    [Fact]
    public void Validar_HmacIncorreto_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");
        var assinaturaCorreta = ComputeHmac(Secret, payload);
        // Adultera 1 char
        var assinaturaErrada = "0" + assinaturaCorreta[1..];

        validator.EhValido(payload, assinaturaErrada, Secret).Should().BeFalse();
    }

    [Fact]
    public void Validar_HmacComSecretDiferente_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");
        var assinaturaComOutroSecret = ComputeHmac("outro-secret", payload);

        validator.EhValido(payload, assinaturaComOutroSecret, Secret).Should().BeFalse();
    }

    [Fact]
    public void Validar_AssinaturaVazia_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");

        validator.EhValido(payload, string.Empty, Secret).Should().BeFalse();
    }

    [Fact]
    public void Validar_PayloadVazio_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var assinatura = ComputeHmac(Secret, Array.Empty<byte>());

        validator.EhValido(Array.Empty<byte>(), assinatura, Secret).Should().BeFalse();
    }

    [Fact]
    public void Validar_SecretVazio_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");
        var assinatura = ComputeHmac("nao-importa", payload);

        validator.EhValido(payload, assinatura, string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Validar_AssinaturaInvalidaHex_RetornaFalse()
    {
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");

        validator.EhValido(payload, "nao-eh-hex-valido-zzzz", Secret).Should().BeFalse();
    }

    [Fact]
    public void Validar_AssinaturaComUpperCase_RetornaTrue()
    {
        // Vetor: MP pode mandar em uppercase. Comparação deve ignorar caso, mas
        // ser constant-time pra hashes equivalentes.
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"123\"}");
        var assinatura = ComputeHmac(Secret, payload).ToUpperInvariant();

        validator.EhValido(payload, assinatura, Secret).Should().BeTrue();
    }

    [Fact]
    public void Validar_VetorConhecido_BateComOpenSSL()
    {
        // HMAC-SHA256 vetor de teste:
        //   key:  "key"
        //   data: "The quick brown fox jumps over the lazy dog"
        //   hex:  f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8
        var validator = new MpHmacValidator();
        var payload = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        const string assinaturaConhecida = "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8";

        validator.EhValido(payload, assinaturaConhecida, "key").Should().BeTrue();
    }
}
