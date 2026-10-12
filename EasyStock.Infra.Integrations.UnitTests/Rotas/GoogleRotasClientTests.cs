using System.Net;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Rotas;
using EasyStock.TestHelpers.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Rotas;

/// <summary>
/// Testes do adapter Google Routes API (issue #1274).
/// HTTP é stubado por um <see cref="HttpFalso"/> — nenhuma chamada de rede real.
/// </summary>
public class GoogleRotasClientTests
{
    private const string Chave = "chave-de-teste";

    private static readonly RotaQuery Query = new(
        OrigemLat: -23.5614, OrigemLng: -46.6562, DestinoLat: -23.5874, DestinoLng: -46.6576);

    private static GoogleRotasClient Client(HttpStatusCode status, string body, out HttpFalso handler)
    {
        handler = new HttpFalso();
        handler.QuandoQualquer().Responde(status, body);
        var http = handler.CriarClient("https://routes.test/");
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

        var chamada = handler.UltimaChamada!;
        Assert.Equal(HttpMethod.Post, chamada.Metodo);
        Assert.EndsWith("directions/v2:computeRoutes", chamada.Uri.AbsoluteUri);
        Assert.DoesNotContain(Chave, chamada.Uri.AbsoluteUri);
        Assert.Equal(Chave, chamada.Cabecalho("X-Goog-Api-Key"));
        Assert.Equal("routes.distanceMeters,routes.duration", chamada.Cabecalho("X-Goog-FieldMask"));
        Assert.Contains("\"travelMode\":\"DRIVE\"", chamada.Corpo);
        Assert.Contains("-23.5874", chamada.Corpo);
    }
}
