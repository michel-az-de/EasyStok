using EasyStock.Web.Services;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>#2668: o link do artefato vem relativo da API e o navegador o abre a partir do host do Web.</summary>
public class LinkDaApiTests
{
    [Theory]
    [InlineData("https://api.example.test", "/api/reports/artefatos/tk", "https://api.example.test/api/reports/artefatos/tk")]
    [InlineData("https://api.example.test/", "/api/reports/artefatos/tk", "https://api.example.test/api/reports/artefatos/tk")]
    [InlineData("https://api.example.test", "https://bucket.example.test/run.csv?sig=1", "https://bucket.example.test/run.csv?sig=1")]
    [InlineData(null, "/api/reports/artefatos/tk", "/api/reports/artefatos/tk")]
    [InlineData("", "/api/reports/artefatos/tk", "/api/reports/artefatos/tk")]
    [InlineData("https://api.example.test", null, null)]
    public void Prefixa_so_o_link_relativo(string? publicApi, string? link, string? esperado)
    {
        LinkDaApi.Absoluto(publicApi, link).Should().Be(esperado);
    }
}
