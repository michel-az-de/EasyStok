using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Geocoding;

/// <summary>
/// Testes do adapter Nominatim (frete por raio, ADR-0017, issue #673 S2).
/// HTTP é stubado por um <see cref="StubHandler"/> — nenhuma chamada de rede real.
/// </summary>
public class NominatimGeocodingClientTests
{
    private static NominatimGeocodingClient Client(HttpStatusCode status, string body, out StubHandler handler)
    {
        handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.test/") };
        return new NominatimGeocodingClient(http, NullLogger<NominatimGeocodingClient>.Instance);
    }

    private static GeocodeQuery QueryCompleta() => new(
        Logradouro: "Avenida Paulista",
        Numero: "1578",
        Bairro: "Bela Vista",
        Cidade: "São Paulo",
        Uf: "SP",
        Cep: "01310100");

    [Fact]
    public async Task Casa_com_house_number_eh_confiavel()
    {
        var json = """[{"lat":"-23.5614","lon":"-46.6562","addresstype":"house","address":{"house_number":"1578"}}]""";
        var client = Client(HttpStatusCode.OK, json, out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.NotNull(r);
        Assert.True(r!.Confiavel);
        Assert.True(Math.Abs(r.Lat - (-23.5614)) < 1e-6);
        Assert.True(Math.Abs(r.Lng - (-46.6562)) < 1e-6);
    }

    [Fact]
    public async Task Granularidade_de_rua_nao_eh_confiavel()
    {
        var json = """[{"lat":"-23.56","lon":"-46.65","addresstype":"road","address":{}}]""";
        var client = Client(HttpStatusCode.OK, json, out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.NotNull(r);
        Assert.False(r!.Confiavel);
    }

    [Fact]
    public async Task House_sem_house_number_nao_eh_confiavel()
    {
        var json = """[{"lat":"-23.56","lon":"-46.65","addresstype":"house","address":{}}]""";
        var client = Client(HttpStatusCode.OK, json, out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.NotNull(r);
        Assert.False(r!.Confiavel);
    }

    [Fact]
    public async Task Array_vazio_retorna_null()
    {
        var client = Client(HttpStatusCode.OK, "[]", out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.Null(r);
    }

    [Fact]
    public async Task Erro_http_retorna_null()
    {
        var client = Client(HttpStatusCode.InternalServerError, "", out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.Null(r);
    }

    [Fact]
    public async Task Lat_lon_invalidos_retorna_null()
    {
        var json = """[{"lat":"abc","lon":"xyz","addresstype":"house","address":{"house_number":"1"}}]""";
        var client = Client(HttpStatusCode.OK, json, out _);

        var r = await client.GeocodificarAsync(QueryCompleta());

        Assert.Null(r);
    }

    [Fact]
    public async Task Query_sem_endereco_nao_bate_na_rede()
    {
        var client = Client(HttpStatusCode.OK, "[]", out var handler);

        var r = await client.GeocodificarAsync(new GeocodeQuery(null, null, null, null, null, null));

        Assert.Null(r);
        Assert.Equal(0, handler.Chamadas);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public int Chamadas { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
