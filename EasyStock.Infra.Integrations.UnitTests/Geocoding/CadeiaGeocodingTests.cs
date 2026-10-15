using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using EasyStock.Infra.Integrations.TomTom;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Geocoding;

/// <summary>
/// Cadeia de geocodificação (I8.1, #1658): falha ou resultado não confiável de um provedor cai no
/// seguinte. Os dois primeiros testes usam os adapters reais com HTTP stubado; nenhuma rede.
/// </summary>
public class CadeiaGeocodingTests
{
    private static readonly GeocodeQuery Endereco = new("Avenida Paulista", "1578", "Bela Vista", "São Paulo", "SP", "01310100");

    private const string NominatimCasa =
        """[{"lat":"-23.5615","lon":"-46.6560","addresstype":"house","address":{"house_number":"1578"}}]""";

    private const string TomTomRuaSemNumero =
        """{"results":[{"type":"Street","address":{"streetName":"Avenida Paulista"},"position":{"lat":-23.5000,"lon":-46.6000}}]}""";

    private sealed class Resposta(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public int Chamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Elo(Func<GeocodeResultado?> resposta, bool disponivel = true) : IGeocodingClient
    {
        public int Chamadas { get; private set; }
        public bool Disponivel => disponivel;

        public Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default)
        {
            Chamadas++;
            return Task.FromResult(resposta());
        }
    }

    private static TomTomGeocodingClient TomTom(Resposta http) =>
        new(new HttpClient(http) { BaseAddress = new Uri("https://tomtom.test/") }, "chave-de-teste", NullLogger<TomTomGeocodingClient>.Instance);

    private static NominatimGeocodingClient Nominatim(Resposta http) =>
        new(new HttpClient(http) { BaseAddress = new Uri("https://nominatim.test/") }, NullLogger<NominatimGeocodingClient>.Instance);

    private static CadeiaGeocodingClient Cadeia(params IGeocodingClient[] elos) =>
        new(elos, NullLogger<CadeiaGeocodingClient>.Instance);

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Cadeia_TomTom_falha_cai_em_Nominatim(HttpStatusCode falha)
    {
        var tomtom = new Resposta(falha, "{}");
        var nominatim = new Resposta(HttpStatusCode.OK, NominatimCasa);

        var r = await Cadeia(TomTom(tomtom), Nominatim(nominatim)).GeocodificarAsync(Endereco);

        Assert.NotNull(r);
        Assert.True(r!.Confiavel);
        Assert.Equal(-23.5615, r.Lat, 4);
        Assert.Equal(1, tomtom.Chamadas);
        Assert.Equal(1, nominatim.Chamadas);
    }

    [Fact]
    public async Task Resultado_nao_confiavel_tenta_o_seguinte()
    {
        var tomtom = new Resposta(HttpStatusCode.OK, TomTomRuaSemNumero);
        var nominatim = new Resposta(HttpStatusCode.OK, NominatimCasa);

        var r = await Cadeia(TomTom(tomtom), Nominatim(nominatim)).GeocodificarAsync(Endereco);

        Assert.True(r!.Confiavel);
        Assert.Equal(-23.5615, r.Lat, 4);
        Assert.Equal(1, nominatim.Chamadas);
    }

    [Fact]
    public async Task Primeiro_confiavel_encerra_a_cadeia()
    {
        var primeiro = new Elo(() => new GeocodeResultado(1, 1, Confiavel: true));
        var segundo = new Elo(() => new GeocodeResultado(2, 2, Confiavel: true));

        var r = await Cadeia(primeiro, segundo).GeocodificarAsync(Endereco);

        Assert.Equal(1, r!.Lat);
        Assert.Equal(0, segundo.Chamadas);
    }

    [Fact]
    public async Task Ninguem_confiavel_devolve_o_primeiro_impreciso()
    {
        var r = await Cadeia(
            new Elo(() => null),
            new Elo(() => new GeocodeResultado(2, 2, Confiavel: false)),
            new Elo(() => new GeocodeResultado(3, 3, Confiavel: false))).GeocodificarAsync(Endereco);

        Assert.Equal(2, r!.Lat);
        Assert.False(r.Confiavel);
    }

    [Fact]
    public async Task Ninguem_responde_devolve_nulo()
    {
        Assert.Null(await Cadeia(new Elo(() => null), new Elo(() => null)).GeocodificarAsync(Endereco));
    }

    [Fact]
    public async Task Elo_que_viola_o_contrato_e_lanca_nao_derruba_o_checkout()
    {
        var r = await Cadeia(
            new Elo(() => throw new InvalidOperationException("https://api.tomtom.com/?key=segredo")),
            new Elo(() => new GeocodeResultado(2, 2, Confiavel: true))).GeocodificarAsync(Endereco);

        Assert.Equal(2, r!.Lat);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_interrompe_a_cadeia()
    {
        using var cts = new CancellationTokenSource();
        var segundo = new Elo(() => new GeocodeResultado(2, 2, Confiavel: true));
        var cadeia = Cadeia(new Elo(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }), segundo);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cadeia.GeocodificarAsync(Endereco, cts.Token));
        Assert.Equal(0, segundo.Chamadas);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Disponivel_quando_algum_elo_esta_ligado(bool primeiro, bool segundo, bool esperado)
    {
        var cadeia = Cadeia(new Elo(() => null, primeiro), new Elo(() => null, segundo));

        Assert.Equal(esperado, cadeia.Disponivel);
    }
}
