using System.Net;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Logistica.Lalamove;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Integrations.UnitTests.Logistica;

/// <summary>Frete pela cotação da Lalamove (#1882): HTTP falso com o formato da v3, sem rede.</summary>
public class CotadorFreteLalamoveTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.FromUnixTimeMilliseconds(1545880607433);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly PontoCotacaoFrete Cozinha = new(-23.5613, -46.6565, "Casa da Babá");
    private static readonly PontoCotacaoFrete Cliente = new(-23.5874, -46.6576, "Rua X, 100, Vila Y, São Paulo - SP, 04000000");

    private const string Cidades = """
        {"data":[{"locode":"BR_SAO","name":"São Paulo","services":[
          {"key":"CAR","description":"Carro"},{"key":"LALAGO","description":"Moto"}]}]}
        """;

    private const string Cotacao = """
        {"data":{"quotationId":"1514140994227007571","scheduleAt":"2026-10-17T15:00:00.00Z","expiresAt":"2026-10-17T15:05:00.00Z",
          "serviceType":"LALAGO","language":"pt_BR","isRouteOptimized":false,
          "stops":[{"stopId":"1","coordinates":{"lat":"-23.5613","lng":"-46.6565"},"address":"Casa da Babá"},
                   {"stopId":"2","coordinates":{"lat":"-23.5874","lng":"-46.6576"},"address":"Rua X"}],
          "priceBreakdown":{"base":"12.40","totalExcludePriorityFee":"17.40","total":"17.40","currency":"BRL"},
          "distance":{"value":"4120","unit":"m"}}}
        """;

    private readonly HttpFalso _http = new();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();

    public CotadorFreteLalamoveTests() => Chave("pk_prod_chave", "sk_prod_segredo");

    private void Chave(string apiKey, string secret) =>
        _credenciais.ObterAsync<Dictionary<string, string>>(Empresa, "lalamove", AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["apiKey"] = apiKey, ["apiSecret"] = secret });

    private CotadorFreteLalamove Sut() => new(_http.CriarClient("https://rest.lalamove.com"), _credenciais,
        new MemoryCache(new MemoryCacheOptions()), new RelogioFixo(Agora), NullLogger<CotadorFreteLalamove>.Instance);

    [Fact]
    public async Task Cota_de_moto_assinado_e_devolve_centavos_e_distancia()
    {
        _http.Quando(HttpMethod.Get, "/v3/cities").Responde(HttpStatusCode.OK, Cidades);
        _http.Quando(HttpMethod.Post, "/v3/quotations").Responde(HttpStatusCode.Created, Cotacao);

        var cotacao = await Sut().CotarAsync(Empresa, Cozinha, Cliente);

        cotacao.Should().Be(new CotacaoFreteExterna("lalamove", 1740, 4120));
        var post = _http.Chamadas.Single(c => c.Metodo == HttpMethod.Post);
        post.Cabecalho("Market").Should().Be("BR");
        post.Cabecalho("Authorization").Should().Be(
            $"hmac pk_prod_chave:{Agora.ToUnixTimeMilliseconds()}:" +
            AssinadorLalamove.Assinar("sk_prod_segredo", Agora.ToUnixTimeMilliseconds(), "POST", "/v3/quotations", post.Corpo));

        using var corpo = JsonDocument.Parse(post.Corpo!);
        var data = corpo.RootElement.GetProperty("data");
        data.GetProperty("serviceType").GetString().Should().Be("LALAGO");
        data.GetProperty("language").GetString().Should().Be("pt_BR");
        data.TryGetProperty("scheduleAt", out _).Should().BeFalse("sem scheduleAt a cotação é para agora");
        data.GetProperty("stops").GetArrayLength().Should().Be(2);
        data.GetProperty("stops")[1].GetProperty("coordinates").GetProperty("lat").GetString().Should().Be("-23.5874");
    }

    [Fact]
    public async Task Mesma_corrida_reaproveita_a_cotacao_e_as_cidades()
    {
        _http.Quando(HttpMethod.Get, "/v3/cities").Responde(HttpStatusCode.OK, Cidades);
        _http.Quando(HttpMethod.Post, "/v3/quotations").Responde(HttpStatusCode.Created, Cotacao);
        var sut = Sut();

        await sut.CotarAsync(Empresa, Cozinha, Cliente);
        await sut.CotarAsync(Empresa, Cozinha, Cliente);
        await sut.CotarAsync(Empresa, Cozinha, Cliente with { Lat = -23.6 });

        _http.Chamadas.Count(c => c.Metodo == HttpMethod.Get).Should().Be(1);
        _http.Chamadas.Count(c => c.Metodo == HttpMethod.Post).Should().Be(2);
    }

    [Fact]
    public async Task Sem_chave_de_producao_nao_chama_a_lalamove()
    {
        _credenciais.ObterAsync<Dictionary<string, string>>(default, default!, default, default).ReturnsForAnyArgs((Dictionary<string, string>?)null);

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();
        _http.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Chave_de_teste_gravada_como_producao_nao_cota_preco_para_cliente()
    {
        Chave("pk_test_chave", "sk_test_segredo");

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();
        _http.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Chave_ilegivel_nao_derruba_o_checkout()
    {
        _credenciais.ObterAsync<Dictionary<string, string>>(default, default!, default, default)
            .ThrowsAsyncForAnyArgs(new ChaveMestraAusenteException("falta a KEK"));

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"errors":[{"id":"ERR_OUT_OF_SERVICE_AREA","message":"x"}]}""")]
    [InlineData(HttpStatusCode.PaymentRequired, """{"errors":[{"id":"ERR_INSUFFICIENT_CREDIT"}]}""")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>")]
    public async Task Recusa_da_lalamove_vira_nulo(HttpStatusCode status, string corpo)
    {
        _http.Quando(HttpMethod.Get, "/v3/cities").Responde(HttpStatusCode.OK, Cidades);
        _http.Quando(HttpMethod.Post, "/v3/quotations").Responde(status, corpo);

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();
    }

    [Fact]
    public async Task Timeout_vira_nulo_mas_cancelamento_do_cliente_propaga()
    {
        _http.Quando(HttpMethod.Get, "/v3/cities").Lanca(new TaskCanceledException("timeout"));

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();

        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        var act = () => Sut().CotarAsync(Empresa, Cozinha, Cliente, cancelado.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Conta_sem_moto_nao_cota()
    {
        _http.Quando(HttpMethod.Get, "/v3/cities").Responde(HttpStatusCode.OK,
            """{"data":[{"locode":"BR_SAO","services":[{"key":"VAN","description":"Van"}]}]}""");

        (await Sut().CotarAsync(Empresa, Cozinha, Cliente)).Should().BeNull();
        _http.Chamadas.Should().ContainSingle();
    }

    [Theory]
    [InlineData("MOTORCYCLE", "MOTORCYCLE")]
    [InlineData("LALAGO", "LALAGO")]
    [InlineData("BIKE_MOTO", "BIKE_MOTO")]
    public void Escolhe_a_moto_entre_os_servicos(string chave, string esperada)
    {
        CotadorFreteLalamove.EscolherMoto([new("BR_SAO", null, [new("CAR", "Carro"), new(chave, null)])])
            .Should().Be(esperada);
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
