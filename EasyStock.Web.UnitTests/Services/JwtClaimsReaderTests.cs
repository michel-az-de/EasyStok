using System.Text;
using System.Text.Json;
using EasyStock.Web.Services;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava o leitor de claims JWT — usado por AuthController, SessionRestoreMiddleware
/// e TokenRefreshHandler para extrair sub/nome/nivel/empresaId do payload do token
/// recebido da API. NAO valida assinatura: depende da fonte do token ser confiavel
/// (response direto da API via HTTPS). A validacao de assinatura/exp/iss e
/// responsabilidade do servidor.
/// </summary>
public class JwtClaimsReaderTests
{
    private readonly IJwtClaimsReader _sut = new JwtClaimsReader();

    [Fact]
    public void TryReadClaim_TokenNulo_RetornaNull()
    {
        _sut.TryReadClaim(null!, "sub").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_TokenVazio_RetornaNull()
    {
        _sut.TryReadClaim("", "sub").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_TokenSemPontos_RetornaNull()
    {
        _sut.TryReadClaim("naotem-ponto", "sub").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_PayloadComClaimString_RetornaValor()
    {
        var token = MakeToken(new { sub = "user-123", nivel = "Admin" });

        _sut.TryReadClaim(token, "sub").Should().Be("user-123");
        _sut.TryReadClaim(token, "nivel").Should().Be("Admin");
    }

    [Fact]
    public void TryReadClaim_ClaimAusente_RetornaNull()
    {
        var token = MakeToken(new { sub = "x" });

        _sut.TryReadClaim(token, "empresaId").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_ClaimNaoString_RetornaNull()
    {
        // exp e iat costumam ser numericos no JWT — devem ser ignorados pela API atual.
        var token = MakeToken(new { exp = 12345, ativo = true });

        _sut.TryReadClaim(token, "exp").Should().BeNull();
        _sut.TryReadClaim(token, "ativo").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_PayloadComCharsBase64Url_Decoda()
    {
        // Forca bytes que produzem '+' e '/' no base64 padrao (que viram '-' e '_' no base64url):
        // bytes 0xFB 0xFF 0xBF → base64 "+/+/" → base64url "-_-_"
        var token = MakeToken(new { dado = "vai+gerar/chars-base64=" });

        _sut.TryReadClaim(token, "dado").Should().Be("vai+gerar/chars-base64=");
    }

    [Theory]
    [InlineData(0)] // sem padding
    [InlineData(1)] // padding ==
    [InlineData(2)] // padding =
    public void TryReadClaim_RespeitaPaddingBase64Url(int paddingPattern)
    {
        // Strings com tamanho que forca padding diferente apos base64url-encode.
        // 0 bytes residuais → length%4==0; 1 byte residual → ==; 2 bytes → =
        var nome = paddingPattern switch
        {
            0 => "abc", // 3 bytes → base64 4 chars sem padding
            1 => "a",   // 1 byte → base64 4 chars com ==
            _ => "ab"   // 2 bytes → base64 4 chars com =
        };
        var token = MakeToken(new { nome });

        _sut.TryReadClaim(token, "nome").Should().Be(nome);
    }

    [Fact]
    public void TryReadClaim_PayloadCorrompidoBase64_RetornaNull()
    {
        _sut.TryReadClaim("header.!!!nao-base64!!!.sig", "sub").Should().BeNull();
    }

    [Fact]
    public void TryReadClaim_PayloadJsonInvalido_RetornaNull()
    {
        var lixo = Base64UrlEncode("nao eh json valido {{");

        _sut.TryReadClaim($"header.{lixo}.sig", "sub").Should().BeNull();
    }

    // -------- helpers --------

    private static string MakeToken(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        var payloadStr = Base64UrlEncode(JsonSerializer.Serialize(payload));
        return $"{header}.{payloadStr}.assinatura-fake-nao-validada";
    }

    private static string Base64UrlEncode(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
