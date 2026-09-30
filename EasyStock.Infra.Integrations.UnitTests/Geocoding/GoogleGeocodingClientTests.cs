using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Geocoding;

/// <summary>
/// Testes do adapter Google Geocoding (módulo de entregas, issue #1217).
/// HTTP é stubado por um <see cref="StubHandler"/> — nenhuma chamada de rede real.
/// </summary>
public class GoogleGeocodingClientTests
{
    private const string Chave = "chave-de-teste";

    private static GoogleGeocodingClient Client(HttpStatusCode status, string body, out StubHandler handler)
    {
        handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://maps.test/") };
        return new GoogleGeocodingClient(http, Chave, NullLogger<GoogleGeocodingClient>.Instance);
    }

    private static GeocodeQuery QueryCompleta() => new(
        Logradouro: "Avenida Paulista",
        Numero: "1578",
        Bairro: "Bela Vista",
        Cidade: "São Paulo",
        Uf: "SP",
        Cep: "01310100");

    private static string Resposta(string locationType, bool comNumero) => $$"""
        {"status":"OK","results":[{
          "geometry":{"location":{"lat":-23.5614,"lng":-46.6562},"location_type":"{{locationType}}"},
          "address_components":[{{(comNumero ? """{"long_name":"1578","types":["street_number"]},""" : "")}}
            {"long_name":"Avenida Paulista","types":["route"]}]
        }]}
        """;

    [Fact]
    public async Task Rooftop_com_numero_eh_confiavel()
    {
        var client = Client(HttpStatusCode.OK, Resposta("ROOFTOP", comNumero: true), out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.NotNull(r);
        Assert.True(r!.Confiavel);
        Assert.True(Math.Abs(r.Lat - (-23.5614)) < 1e-6);
        Assert.True(Math.Abs(r.Lng - (-46.6562)) < 1e-6);
    }

    [Fact]
    public async Task Interpolado_com_numero_eh_confiavel()
    {
        var client = Client(HttpStatusCode.OK, Resposta("RANGE_INTERPOLATED", comNumero: true), out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.True(r!.Confiavel);
    }

    [Fact]
    public async Task Centro_geometrico_nao_eh_confiavel()
    {
        var client = Client(HttpStatusCode.OK, Resposta("GEOMETRIC_CENTER", comNumero: true), out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.NotNull(r);
        Assert.False(r!.Confiavel);
    }

    [Fact]
    public async Task Rooftop_sem_numero_nao_eh_confiavel()
    {
        var client = Client(HttpStatusCode.OK, Resposta("ROOFTOP", comNumero: false), out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.False(r!.Confiavel);
    }

    [Theory]
    [InlineData("""{"status":"ZERO_RESULTS","results":[]}""")]
    [InlineData("""{"status":"REQUEST_DENIED","error_message":"x","results":[]}""")]
    [InlineData("""{"status":"OVER_QUERY_LIMIT","results":[]}""")]
    [InlineData("não é json")]
    public async Task Status_nao_ok_ou_json_invalido_retorna_null(string body)
    {
        var client = Client(HttpStatusCode.OK, body, out _);

        Assert.Null(await client.GeocodificarAsync(QueryCompleta()));
    }

    [Fact]
    public async Task Erro_http_retorna_null()
    {
        var client = Client(HttpStatusCode.InternalServerError, "", out _);

        Assert.Null(await client.GeocodificarAsync(QueryCompleta()));
    }

    [Fact]
    public async Task Query_sem_endereco_nao_bate_na_rede()
    {
        var client = Client(HttpStatusCode.OK, "{}", out var handler);

        var r = await client.GeocodificarAsync(new GeocodeQuery(null, null, null, null, null, null));

        Assert.Null(r);
        Assert.Equal(0, handler.Chamadas);
    }

    [Fact]
    public async Task Requisicao_restringe_ao_brasil_e_envia_chave()
    {
        var client = Client(HttpStatusCode.OK, Resposta("ROOFTOP", comNumero: true), out var handler);

        await client.GeocodificarAsync(QueryCompleta());

        var url = handler.UltimaUrl!;
        Assert.Contains("geocode/json?", url);
        Assert.Contains("components=country%3ABR", url);
        Assert.Contains($"key={Chave}", url);
        Assert.Contains("Avenida%20Paulista", url);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public int Chamadas { get; private set; }
        public string? UltimaUrl { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            UltimaUrl = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
