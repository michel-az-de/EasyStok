using EasyStock.Application.UseCases.GerenciarUploads;

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

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void SanitizeFileName_rejeita_apenas_pontos(string input)
    {
        var act = () => UploadSecurityValidator.SanitizeFileName(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("arquivo<html>.jpg")]
    [InlineData("arquivo>script.jpg")]
    [InlineData("arq:uivo.jpg")]
    [InlineData("arq|uivo.jpg")]
    [InlineData("arq?uivo.jpg")]
    [InlineData("arq*uivo.jpg")]
    public void SanitizeFileName_rejeita_caracteres_invalidos_windows(string input)
    {
        var act = () => UploadSecurityValidator.SanitizeFileName(input);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*caractere invalido*");
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
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
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

    // ── EnsureContentMatchesDeclaredType — assinatura de bytes ───────────────

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static byte[] Gif() => [0x47, 0x49, 0x46, 0x38, 0x39, 0x61];
    private static byte[] Webp() => [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];
    private static byte[] Pdf() => [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];

    [Fact]
    public void EnsureContentMatchesDeclaredType_aceita_assinaturas_validas()
    {
        var checks = new Action[]
        {
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Jpeg(), "image/jpeg"),
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Png(), "image/png"),
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Gif(), "image/gif"),
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Webp(), "image/webp"),
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Pdf(), "application/pdf"),
            () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Jpeg(), "image/jpeg; charset=binary"),
        };
        foreach (var check in checks)
            check.Should().NotThrow();
    }

    [Fact]
    public void EnsureContentMatchesDeclaredType_rejeita_html_disfarcado_de_jpeg()
    {
        var html = System.Text.Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");
        var act = () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(html, "image/jpeg");
        act.Should().Throw<InvalidOperationException>().WithMessage("*nao corresponde*");
    }

    [Fact]
    public void EnsureContentMatchesDeclaredType_rejeita_png_declarado_com_bytes_de_jpeg()
    {
        var act = () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(Jpeg(), "image/png");
        act.Should().Throw<InvalidOperationException>().WithMessage("*nao corresponde*");
    }

    [Fact]
    public void EnsureContentMatchesDeclaredType_aceita_csv_sem_assinatura_conhecida()
    {
        var csv = System.Text.Encoding.UTF8.GetBytes("nome,preco\nProduto,10.00");
        var act = () => UploadSecurityValidator.EnsureContentMatchesDeclaredType(csv, "text/csv");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureContentMatchesDeclaredType_rejeita_conteudo_vazio()
    {
        var act = () => UploadSecurityValidator.EnsureContentMatchesDeclaredType([], "image/png");
        act.Should().Throw<InvalidOperationException>();
    }
}
