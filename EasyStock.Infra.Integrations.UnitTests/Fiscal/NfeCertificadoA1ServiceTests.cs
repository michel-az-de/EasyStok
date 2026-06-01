using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Fiscal;

/// <summary>
/// Testes de ESPECIFICAÇÃO (afirmam o comportamento CORRETO, não o atual) de
/// <see cref="NfeCertificadoA1Service.ValidarUpload"/> — Track A1 do plano de dívida
/// técnica (rede de testes em Infra.Integrations, antes sem cobertura). Refs #274.
///
/// Oráculo do "correto": regras X509 — um cert A1 só é aceito se parseável com a
/// senha dada E ainda dentro da validade (NotAfter). Senha errada, pfx/senha vazios
/// ou cert expirado DEVEM ser rejeitados com <see cref="InvalidOperationException"/>.
/// </summary>
public class NfeCertificadoA1ServiceTests
{
    // ValidarUpload não toca IDataProtectionProvider (caminho puramente X509) —
    // null é seguro aqui. O round-trip Cifrar/Decifrar (que usa data protection)
    // vem na fatia 2 com EphemeralDataProtectionProvider.
    private static NfeCertificadoA1Service Sut() =>
        new(dataProtection: null!, logger: NullLogger<NfeCertificadoA1Service>.Instance);

    /// <summary>Gera um .pfx self-signed com a validade pedida, protegido por senha.</summary>
    private static byte[] GerarPfx(DateTimeOffset notBefore, DateTimeOffset notAfter, string senha)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=EasyStok Teste A1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        return cert.Export(X509ContentType.Pkcs12, senha);
    }

    [Fact]
    public void ValidarUpload_cert_valido_retorna_NotAfter()
    {
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);
        var pfx = GerarPfx(DateTimeOffset.UtcNow.AddDays(-1), notAfter, "s3nha");

        var validade = Sut().ValidarUpload(pfx, "s3nha");

        validade.ToUniversalTime().Should().BeCloseTo(notAfter.UtcDateTime, TimeSpan.FromDays(1));
    }

    [Fact]
    public void ValidarUpload_cert_expirado_rejeita()
    {
        var pfx = GerarPfx(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1), "s3nha");

        var act = () => Sut().ValidarUpload(pfx, "s3nha");

        act.Should().Throw<InvalidOperationException>().WithMessage("*expirado*");
    }

    [Fact]
    public void ValidarUpload_senha_errada_rejeita()
    {
        var pfx = GerarPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), "certa");

        var act = () => Sut().ValidarUpload(pfx, "errada");

        act.Should().Throw<InvalidOperationException>().WithMessage("*invalido*");
    }

    [Fact]
    public void ValidarUpload_pfx_vazio_rejeita()
    {
        var act = () => Sut().ValidarUpload(Array.Empty<byte>(), "s3nha");

        act.Should().Throw<InvalidOperationException>().WithMessage("*PfxBytes*");
    }

    [Fact]
    public void ValidarUpload_senha_vazia_rejeita()
    {
        var pfx = GerarPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), "s3nha");

        var act = () => Sut().ValidarUpload(pfx, "");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Senha*");
    }
}
