using EasyStock.Application.UseCases.GerenciarUploads;
using FluentAssertions;

namespace EasyStock.Application.Tests.UseCases;

public class UploadSecurityValidatorTests
{
    // ── SanitizeFileName — aceitos ───────────────────────────────────────────

    [Theory]
    [InlineData("foto.jpg", "foto.jpg")]
    [InlineData("meu-arquivo.webp", "meu-arquivo.webp")]
    [InlineData("planilha 2026.csv", "planilha 2026.csv")]
    [InlineData("  foto.jpg  ", "foto.jpg")]
    public void SanitizeFileName_aceita_nomes_validos(string input, string expected)
    {
        UploadSecurityValidator.SanitizeFileName(input).Should().Be(expected);
    }

    // ── SanitizeFileName — rejeitados ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFileName_rejeita_vazio(string? input)
    {
        var act = () => UploadSecurityValidator.SanitizeFileName(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\windows\\system32")]
    [InlineData("foo/../bar.jpg")]
    [InlineData("....//foo.jpg")]
    public void SanitizeFileName_rejeita_path_traversal(string input)
    {
        var act = () => UploadSecurityValidator.SanitizeFileName(input);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*path traversal*");
    }

    [Fact]
    public void SanitizeFileName_rejeita_caractere_null()
    {
        var act = () => UploadSecurityValidator.SanitizeFileName("foo\0bar.jpg");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*null*");
    }

    [Fact]
    public void SanitizeFileName_rejeita_nome_excessivamente_longo()
    {
        var longName = new string('a', 260) + ".jpg";
        var act = () => UploadSecurityValidator.SanitizeFileName(longName);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*255*");
    }

    // ── EnsureValidMime — aceitos ────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/csv")]
    [InlineData("text/csv; charset=utf-8")]                                     // com parâmetro
    [InlineData("IMAGE/JPEG")]                                                   // case insensitive
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void EnsureValidMime_aceita_whitelist(string contentType)
    {
        var act = () => UploadSecurityValidator.EnsureValidMime(contentType);
        act.Should().NotThrow();
    }

    // ── EnsureValidMime — rejeitados ─────────────────────────────────────────

    [Theory]
    [InlineData("application/x-msdownload")]    // .exe
    [InlineData("application/javascript")]
    [InlineData("text/html")]
    [InlineData("application/x-sh")]
    [InlineData("application/octet-stream")]
    public void EnsureValidMime_rejeita_tipos_perigosos(string contentType)
    {
        var act = () => UploadSecurityValidator.EnsureValidMime(contentType);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nao permitido*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureValidMime_rejeita_vazio(string? contentType)
    {
        var act = () => UploadSecurityValidator.EnsureValidMime(contentType);
        act.Should().Throw<InvalidOperationException>();
    }
}
