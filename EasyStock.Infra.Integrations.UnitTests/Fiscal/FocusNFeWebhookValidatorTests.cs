using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.Fiscal;

/// <summary>
/// Spec-tests do <see cref="FocusNFeWebhookValidator"/> — Track A1, refs #274.
/// É a fronteira que impede spoofing de callback (um atacante forjando "autorizado"
/// para a SEFAZ). Oráculo: só aceita header cuja HMAC-SHA256(secret, body) bate;
/// secret ausente, header ausente/inválido, body vazio ou hash divergente DEVEM
/// rejeitar. Bug aqui = aceitar webhook forjado, então é caminho de segurança.
/// </summary>
public class FocusNFeWebhookValidatorTests
{
    private const string Secret = "focus-webhook-secret-123";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("corpo-do-webhook-focus");

    private static FocusNFeWebhookValidator Sut(string? secret = Secret) =>
        new(Options.Create(new FocusNFeOptions { WebhookSecret = secret }));

    private static string AssinarHex(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    [Fact]
    public void Assinatura_correta_aceita()
    {
        Sut().ValidarAssinatura(AssinarHex(Secret, Body), Body).Should().BeTrue();
    }

    [Fact]
    public void Assinatura_de_outro_body_rejeita()
    {
        var assinaturaDeOutroBody = AssinarHex(Secret, Encoding.UTF8.GetBytes("body diferente"));

        Sut().ValidarAssinatura(assinaturaDeOutroBody, Body).Should().BeFalse();
    }

    [Fact]
    public void Assinatura_com_secret_do_atacante_rejeita()
    {
        var assinaturaForjada = AssinarHex("secret-do-atacante", Body);

        Sut().ValidarAssinatura(assinaturaForjada, Body).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Header_ausente_rejeita(string? header)
    {
        Sut().ValidarAssinatura(header, Body).Should().BeFalse();
    }

    [Fact]
    public void Body_vazio_rejeita()
    {
        Sut().ValidarAssinatura(AssinarHex(Secret, Body), Array.Empty<byte>()).Should().BeFalse();
    }

    [Fact]
    public void Secret_nao_configurado_rejeita()
    {
        // Fail-closed: sem secret configurado, NUNCA aceitar (não há como validar).
        Sut(secret: null).ValidarAssinatura(AssinarHex(Secret, Body), Body).Should().BeFalse();
    }

    [Theory]
    [InlineData("zzzz")] // não é hex
    [InlineData("abc")]  // tamanho ímpar
    public void Header_hex_malformado_rejeita(string header)
    {
        Sut().ValidarAssinatura(header, Body).Should().BeFalse();
    }
}
