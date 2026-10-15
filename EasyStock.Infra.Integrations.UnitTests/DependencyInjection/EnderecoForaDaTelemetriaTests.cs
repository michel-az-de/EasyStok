using System.Diagnostics;
using EasyStock.Infra.Integrations.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Integrations.UnitTests.DependencyInjection;

/// <summary>Quais chamadas têm a URL ocultada no span (I8.2, #1661).</summary>
public class EnderecoForaDaTelemetriaTests
{
    private static string? UrlNoSpan(string url, Dictionary<string, string?>? config = null)
    {
        var ocultar = EnderecoForaDaTelemetria.OcultarUrlNoSpan(
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build());
        using var span = new Activity("GET");
        span.SetTag("url.full", url);

        ocultar(span, new HttpRequestMessage(HttpMethod.Get, url));

        return span.GetTagItem("url.full") as string;
    }

    [Theory]
    [InlineData("https://viacep.com.br/ws/01234987/json/", "https://viacep.com.br/")]
    [InlineData("https://www.viacep.com.br/ws/01234987/json/", "https://www.viacep.com.br/")] // redirecionamento para subdomínio
    [InlineData("https://nominatim.openstreetmap.org/search?street=4242+Rua+X", "https://nominatim.openstreetmap.org/")]
    [InlineData("https://maps.googleapis.com/maps/api/geocode/json?address=Rua+X", "https://maps.googleapis.com/")]
    [InlineData("https://api.tomtom.com/search/2/geocode/Rua%20X%2C%204242.json", "https://api.tomtom.com/")]
    [InlineData("https://API.TOMTOM.COM/routing/1/calculateRoute/-23.5,-46.6:-23.6,-46.7/json", "https://api.tomtom.com/")]
    public void Provedor_de_endereco_fica_so_com_a_origem(string url, string esperado)
    {
        Assert.Equal(esperado, UrlNoSpan(url));
    }

    [Theory]
    [InlineData("https://naoviacep.com.br/ws/01234987/json/")]
    [InlineData("https://api.mercadopago.com/v1/payments/123")]
    [InlineData("https://graph.facebook.com/v21.0/123/messages")]
    public void Outras_chamadas_nao_mudam(string url)
    {
        Assert.Equal(url, UrlNoSpan(url));
    }

    [Fact]
    public void Base_url_configurada_tambem_e_ocultada()
    {
        var config = new Dictionary<string, string?>
        {
            ["Storefront:Frete:NominatimBaseUrl"] = "http://nominatim.interno:8088/",
            ["Storefront:Frete:ViaCepBaseUrl"] = "nao e url",
        };

        Assert.Equal("http://nominatim.interno:8088/", UrlNoSpan("http://nominatim.interno:8088/search?street=4242+Rua+X", config));
    }
}