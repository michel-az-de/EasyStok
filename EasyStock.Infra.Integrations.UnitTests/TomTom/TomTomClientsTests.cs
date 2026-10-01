using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.TomTom;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.TomTom;

/// <summary>
/// Testes dos adapters TomTom (geocoding + rota, issue #1322).
/// HTTP é stubado por um <see cref="StubHandler"/> — nenhuma chamada de rede real.
/// </summary>
public class TomTomClientsTests
{
    private const string Chave = "chave-de-teste";

    private static readonly GeocodeQuery Endereco = new(
        Logradouro: "Avenida Paulista",
        Numero: "1578",
        Bairro: "Bela Vista",
        Cidade: "São Paulo",
        Uf: "SP",
        Cep: "01310100");

    private static readonly RotaQuery Trecho = new(-23.5614, -46.6562, -23.5874, -46.6576);

    private static HttpClient Http(HttpStatusCode status, string body, out StubHandler handler)
    {
        handler = new StubHandler(status, body);
        return new HttpClient(handler) { BaseAddress = new Uri("https://tomtom.test/") };
    }

    private static TomTomGeocodingClient Geo(HttpStatusCode status, string body, out StubHandler handler) =>
        new(Http(status, body, out handler), Chave, NullLogger<TomTomGeocodingClient>.Instance);

    private static TomTomRotasClient Rotas(HttpStatusCode status, string body, out StubHandler handler) =>
        new(Http(status, body, out handler), Chave, NullLogger<TomTomRotasClient>.Instance);

    private static string Geocode(string tipo, bool comNumero)
    {
        var endereco = comNumero
            ? """{"streetNumber":"1578","streetName":"Avenida Paulista"}"""
            : """{"streetName":"Avenida Paulista"}""";
        return "{\"results\":[{\"type\":\"" + tipo + "\",\"address\":" + endereco
               + ",\"position\":{\"lat\":-23.5614,\"lon\":-46.6562}}]}";
    }

    // ── Geocoding ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Point Address")]
    [InlineData("Address Range")]
    public async Task Endereco_com_numero_eh_confiavel(string tipo)
    {
        var r = await Geo(HttpStatusCode.OK, Geocode(tipo, comNumero: true), out _).GeocodificarAsync(Endereco);

        Assert.NotNull(r);
        Assert.True(r!.Confiavel);
        Assert.True(Math.Abs(r.Lat - (-23.5614)) < 1e-6);
        Assert.True(Math.Abs(r.Lng - (-46.6562)) < 1e-6);
    }

    [Fact]
    public async Task Rua_sem_numero_nao_eh_confiavel()
    {
        var r = await Geo(HttpStatusCode.OK, Geocode("Street", comNumero: false), out _).GeocodificarAsync(Endereco);

        Assert.NotNull(r);
        Assert.False(r!.Confiavel);
    }

    [Fact]
    public async Task Point_address_sem_numero_nao_eh_confiavel()
    {
        var r = await Geo(HttpStatusCode.OK, Geocode("Point Address", comNumero: false), out _).GeocodificarAsync(Endereco);

        Assert.False(r!.Confiavel);
    }

    [Theory]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"results":[{"type":"Point Address","address":{}}]}""")]
    [InlineData("não é json")]
    public async Task Geocode_sem_resultado_ou_invalido_retorna_null(string body)
    {
        Assert.Null(await Geo(HttpStatusCode.OK, body, out _).GeocodificarAsync(Endereco));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Geocode_erro_http_retorna_null(HttpStatusCode status)
    {
        Assert.Null(await Geo(status, "{}", out _).GeocodificarAsync(Endereco));
    }

    [Fact]
    public async Task Geocode_sem_endereco_nao_bate_na_rede()
    {
        var client = Geo(HttpStatusCode.OK, "{}", out var handler);

        Assert.Null(await client.GeocodificarAsync(new GeocodeQuery(null, null, null, null, null, null)));
        Assert.Equal(0, handler.Chamadas);
    }

    [Fact]
    public async Task Geocode_restringe_ao_brasil()
    {
        var client = Geo(HttpStatusCode.OK, Geocode("Point Address", true), out var handler);

        await client.GeocodificarAsync(Endereco);

        Assert.Contains("search/2/geocode/", handler.Url);
        Assert.Contains("countrySet=BR", handler.Url);
        Assert.Contains("Avenida%20Paulista", handler.Url);
        Assert.Contains($"key={Chave}", handler.Url);
    }

    // ── Rota ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rota_ok_devolve_distancia_e_duracao()
    {
        var body = """{"routes":[{"summary":{"lengthInMeters":3870,"travelTimeInSeconds":612}}]}""";

        var r = await Rotas(HttpStatusCode.OK, body, out _).MedirAsync(Trecho);

        Assert.NotNull(r);
        Assert.Equal(3870, r!.DistanciaMetros);
        Assert.Equal(612, r.DuracaoSegundos);
    }

    [Theory]
    [InlineData("""{"routes":[]}""")]
    [InlineData("""{"routes":[{"summary":{"travelTimeInSeconds":612}}]}""")]
    [InlineData("não é json")]
    public async Task Rota_sem_resultado_ou_invalida_retorna_null(string body)
    {
        Assert.Null(await Rotas(HttpStatusCode.OK, body, out _).MedirAsync(Trecho));
    }

    [Fact]
    public async Task Rota_erro_http_retorna_null()
    {
        Assert.Null(await Rotas(HttpStatusCode.BadRequest, "{}", out _).MedirAsync(Trecho));
    }

    [Fact]
    public async Task Rota_pede_carro_sem_transito_com_coordenadas_invariantes()
    {
        var body = """{"routes":[{"summary":{"lengthInMeters":1,"travelTimeInSeconds":1}}]}""";
        var client = Rotas(HttpStatusCode.OK, body, out var handler);

        await client.MedirAsync(Trecho);

        Assert.Contains("routing/1/calculateRoute/-23.5614,-46.6562:-23.5874,-46.6576/json", handler.Url);
        Assert.Contains("travelMode=car", handler.Url);
        Assert.Contains("traffic=false", handler.Url);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public int Chamadas { get; private set; }
        public string Url { get; private set; } = "";

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            Url = request.RequestUri?.AbsoluteUri ?? "";
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
