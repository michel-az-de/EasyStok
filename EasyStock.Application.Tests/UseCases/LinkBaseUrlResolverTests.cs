namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Trava o sanitizador de BaseUrl dos links de e-mail (password reset poisoning #765):
/// so devolve o host quando ele esta na allowlist; host arbitrario => null.
/// </summary>
public class LinkBaseUrlResolverTests
{
    private static readonly string[] Confiaveis =
    {
        "https://easystok-web.onrender.com",
        "http://localhost:5173"
    };

    [Fact]
    public void Host_NaoConfiavel_Retorna_Null()
    {
        LinkBaseUrlResolver.ResolveTrusted("https://evil.com", Confiaveis).Should().BeNull();
    }

    [Fact]
    public void Host_Confiavel_Retorna_BaseUrl_SemBarraFinal()
    {
        LinkBaseUrlResolver.ResolveTrusted("https://easystok-web.onrender.com/", Confiaveis)
            .Should().Be("https://easystok-web.onrender.com");
    }

    [Fact]
    public void Host_Confiavel_ComPath_Preserva_Comparando_SoOrigem()
    {
        LinkBaseUrlResolver.ResolveTrusted("http://localhost:5173/qualquer", Confiaveis)
            .Should().Be("http://localhost:5173/qualquer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nao-e-url")]
    public void BaseUrl_Vazia_Ou_Invalida_Retorna_Null(string? baseUrl)
    {
        LinkBaseUrlResolver.ResolveTrusted(baseUrl, Confiaveis).Should().BeNull();
    }

    [Fact]
    public void Sem_Origens_Confiaveis_Retorna_Null()
    {
        LinkBaseUrlResolver.ResolveTrusted("https://easystok-web.onrender.com", (IEnumerable<string?>?)null).Should().BeNull();
    }

    [Fact]
    public void Porta_Divergente_Nao_Confia()
    {
        LinkBaseUrlResolver.ResolveTrusted("https://easystok-web.onrender.com:8443", Confiaveis).Should().BeNull();
    }
}
