using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Rotas;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Rotas;

/// <summary>
/// Testes do adapter Google Routes API (issue #1274).
/// HTTP é stubado por um <see cref="StubHandler"/> — nenhuma chamada de rede real.
/// </summary>
public class GoogleRotasClientTests
{
    private const string Chave = "chave-de-teste";

    private static readonly RotaQuery Query = new(
        OrigemLat: -23.5614, OrigemLng: -46.6562, DestinoLat: -23.5874, DestinoLng: -46.6576);

    private static GoogleRotasClient Client(HttpStatusCode status, string body, out StubHandler handler)
    {
        handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://routes.test/") };
        return new GoogleRotasClient(http, Chave, NullLogger<GoogleRotasClient>.Instance);
    }

    [Fact]
    public async Task Rota_ok_devolve_distancia_e_duracao()
    {
        var client = Client(HttpStatusCode.OK, """{"routes":[{"distanceMeters":3870,"duration":"612s"}]}""", out _);

        var r = await client.MedirAsync(Query);

        Assert.NotNull(r);
        Assert.Equal(3870, r!.DistanciaMetros);
        Assert.Equal(612, r.DuracaoSegundos);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"routes":[]}""")]
    [InlineData("""{"routes":[{"duration":"612s"}]}""")]
    [InlineData("""{"routes":[{"distanceMeters":3870,"duration":"abc"}]}""")]
    [InlineData("não é json")]
    public async Task Resposta_sem_rota_ou_invalida_retorna_null(string body)
    {
        var client = Client(HttpStatusCode.OK, body, out _);

        Assert.Null(await client.MedirAsync(Query));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Erro_http_retorna_null(HttpStatusCode status)
    {
        var client = Client(status, """{"error":{"message":"x"}}""", out _);

        Assert.Null(await client.MedirAsync(Query));
    }

    [Fact]
    public async Task Requisicao_usa_post_com_chave_no_header_e_nunca_na_url()
    {
        var client = Client(HttpStatusCode.OK, """{"routes":[{"distanceMeters":1,"duration":"1s"}]}""", out var handler);

        await client.MedirAsync(Query);

        Assert.Equal(HttpMethod.Post, handler.Metodo);
        Assert.EndsWith("directions/v2:computeRoutes", handler.Url);
        Assert.DoesNotContain(Chave, handler.Url);
        Assert.Equal(Chave, handler.Header("X-Goog-Api-Key"));
        Assert.Equal("routes.distanceMeters,routes.duration", handler.Header("X-Goog-FieldMask"));
        Assert.Contains("\"travelMode\":\"DRIVE\"", handler.Corpo);
        Assert.Contains("-23.5874", handler.Corpo);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private HttpRequestMessage? _ultima;

        public HttpMethod? Metodo => _ultima?.Method;
        public string Url => _ultima?.RequestUri?.AbsoluteUri ?? "";
        public string Corpo { get; private set; } = "";

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? Header(string nome) =>
            _ultima is not null && _ultima.Headers.TryGetValues(nome, out var v) ? v.Single() : null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ultima = request;
            Corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
