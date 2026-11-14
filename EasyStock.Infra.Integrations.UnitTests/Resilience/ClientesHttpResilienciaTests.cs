using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Ia;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.Infra.Integrations.Rotas;
using EasyStock.Infra.Integrations.TomTom;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.Resilience;

/// <summary>
/// #2177: os clientes de pagamento, rota e IA passam pelos pipelines de <c>AddEasyStockIntegrationResilience</c>.
/// Leitura repete resposta transitória (5xx); escrita não idempotente só tem disjuntor e timeout.
/// </summary>
public class ClientesHttpResilienciaTests
{
    private const string PagamentoJson = """{"id":20359978,"status":"approved","transaction_amount":25.5}""";
    private const string PreferenciaJson = """{"id":"pref-1","init_point":"https://mp.test/pref-1"}""";
    private const string RotaGoogleJson = """{"routes":[{"distanceMeters":3870,"duration":"612s"}]}""";
    private const string RotaTomTomJson = """{"routes":[{"summary":{"lengthInMeters":3870,"travelTimeInSeconds":612}}]}""";
    private const string GeocodeTomTomJson = """
        {"results":[{"type":"Point Address","address":{"streetNumber":"1578","streetName":"Avenida Paulista"},
          "position":{"lat":-23.5614,"lon":-46.6562}}]}
        """;

    private static readonly RotaQuery Trecho = new(-23.5614, -46.6562, -23.5874, -46.6576);
    private static readonly GeocodeQuery Endereco = new("Avenida Paulista", "1578", "Bela Vista", "São Paulo", "SP", "01310100");

    private static HttpFalso FalsoQueFalhaDuasVezes(string corpo)
    {
        var falso = new HttpFalso();
        falso.QuandoQualquer()
            .Responde(HttpStatusCode.ServiceUnavailable)
            .Responde(HttpStatusCode.ServiceUnavailable)
            .Responde(HttpStatusCode.OK, corpo);
        return falso;
    }

    private static HttpFalso FalsoFora()
    {
        var falso = new HttpFalso();
        falso.QuandoQualquer().Responde(HttpStatusCode.ServiceUnavailable);
        return falso;
    }

    private static MercadoPagoClient MercadoPago(HttpFalso falso, ResiliencePipelineProvider<string>? pipelines = null) =>
        new(falso.CriarClient("https://api.mp.test/"), Options.Create(new MercadoPagoOptions { AccessToken = "fake" }),
            pipelines ?? PipelinesDeTeste.Reais(), NullLogger<MercadoPagoClient>.Instance);

    [Fact]
    public async Task Consulta_do_pagamento_recupera_depois_de_dois_503()
    {
        var falso = FalsoQueFalhaDuasVezes(PagamentoJson);

        var pagamento = await MercadoPago(falso).ConsultarPagamentoAsync("20359978");

        pagamento!.Status.Should().Be("approved");
        falso.Chamadas.Should().HaveCount(3, "GET é idempotente e repete a resposta transitória");
    }

    [Fact]
    public async Task Criar_preferencia_com_503_nao_repete()
    {
        var falso = FalsoQueFalhaDuasVezes(PreferenciaJson);
        var comando = new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Casa da Baba", 10m, [new PreferenceItemCommand("Brigadeiro", 1, 10m)]);

        var act = () => MercadoPago(falso).CriarPreferenceAsync(comando);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        falso.Chamadas.Should().ContainSingle("POST de cobrança não é idempotente e não repete sozinho");
    }

    [Fact]
    public async Task Estorno_com_503_nao_repete()
    {
        var falso = FalsoQueFalhaDuasVezes("""{"id":1}""");

        var act = () => MercadoPago(falso).EstornarAsync("20359978", 10m, "operacao-1");

        await act.Should().ThrowAsync<HttpRequestException>();
        falso.Chamadas.Should().ContainSingle();
    }

    [Fact]
    public async Task Disjuntor_aberto_na_escrita_vira_falha_de_conexao_e_nao_fecha_a_leitura()
    {
        var pipelines = PipelinesDeTeste.Reais();
        var fora = FalsoFora();
        var client = MercadoPago(fora, pipelines);
        for (var i = 0; i < 8; i++)
            await FluentActions.Awaiting(() => client.EstornarAsync("20359978", null, $"op-{i}")).Should().ThrowAsync<HttpRequestException>();

        var aberto = await FluentActions.Awaiting(() => client.EstornarAsync("20359978", null, "op-9"))
            .Should().ThrowAsync<HttpRequestException>();

        aberto.Which.HttpRequestError.Should().Be(HttpRequestError.ConnectionError);
        fora.Chamadas.Should().HaveCount(8, "com o disjuntor aberto o POST nem sai");
        var leitura = new HttpFalso();
        leitura.QuandoQualquer().Responde(HttpStatusCode.OK, PagamentoJson);
        (await MercadoPago(leitura, pipelines).ConsultarPagamentoAsync("20359978")).Should().NotBeNull(
            "a falha da escrita não abre o disjuntor da leitura");
    }

    [Fact]
    public async Task Rota_do_google_recupera_depois_de_um_503()
    {
        var falso = new HttpFalso();
        falso.QuandoQualquer().Responde(HttpStatusCode.ServiceUnavailable).Responde(HttpStatusCode.OK, RotaGoogleJson);
        var client = new GoogleRotasClient(falso.CriarClient("https://routes.test/"), "chave", PipelinesDeTeste.Reais(),
            NullLogger<GoogleRotasClient>.Instance);

        var rota = await client.MedirAsync(Trecho);

        rota!.DistanciaMetros.Should().Be(3870);
        falso.Chamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rota_do_google_com_disjuntor_aberto_devolve_null_sem_chamar()
    {
        var fora = FalsoFora();
        var client = new GoogleRotasClient(fora.CriarClient("https://routes.test/"), "chave", PipelinesDeTeste.Reais(),
            NullLogger<GoogleRotasClient>.Instance);
        for (var i = 0; i < 4; i++)
            (await client.MedirAsync(Trecho)).Should().BeNull();
        var antes = fora.Chamadas.Count;

        (await client.MedirAsync(Trecho)).Should().BeNull("o frete cai para a estimativa sem esperar o Google");

        fora.Chamadas.Should().HaveCount(antes, "com o disjuntor aberto a chamada nem sai");
    }

    [Fact]
    public async Task Rota_e_geocoding_da_tomtom_recuperam_depois_de_um_503()
    {
        var pipelines = PipelinesDeTeste.Reais();
        var rotas = new HttpFalso();
        rotas.QuandoQualquer().Responde(HttpStatusCode.ServiceUnavailable).Responde(HttpStatusCode.OK, RotaTomTomJson);
        var geo = new HttpFalso();
        geo.QuandoQualquer().Responde(HttpStatusCode.ServiceUnavailable).Responde(HttpStatusCode.OK, GeocodeTomTomJson);

        var rota = await new TomTomRotasClient(rotas.CriarClient("https://tomtom.test/"), "chave", pipelines,
            NullLogger<TomTomRotasClient>.Instance).MedirAsync(Trecho);
        var ponto = await new TomTomGeocodingClient(geo.CriarClient("https://tomtom.test/"), "chave", pipelines,
            NullLogger<TomTomGeocodingClient>.Instance).GeocodificarAsync(Endereco);

        rota!.DuracaoSegundos.Should().Be(612);
        ponto!.Confiavel.Should().BeTrue();
        rotas.Chamadas.Should().HaveCount(2);
        geo.Chamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Anthropic_com_503_nao_repete_e_com_disjuntor_aberto_nao_chama()
    {
        var fora = FalsoFora();
        var client = new AnthropicMessagesClient(fora.CriarClient("https://anthropic.test/"),
            Options.Create(new AnthropicAgenteOptions { Enabled = true, ApiKey = "chave-de-teste" }), PipelinesDeTeste.Reais(),
            NullLogger<AnthropicMessagesClient>.Instance);
        var requisicao = new RequisicaoLlm("sistema", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []);

        await FluentActions.Awaiting(() => client.EnviarAsync(requisicao)).Should().ThrowAsync<HttpRequestException>();
        fora.Chamadas.Should().ContainSingle("POST sem chave de idempotência não repete");
        for (var i = 1; i < 8; i++)
            await FluentActions.Awaiting(() => client.EnviarAsync(requisicao)).Should().ThrowAsync<HttpRequestException>();

        var aberto = await FluentActions.Awaiting(() => client.EnviarAsync(requisicao)).Should().ThrowAsync<HttpRequestException>();

        aberto.Which.HttpRequestError.Should().Be(HttpRequestError.ConnectionError);
        fora.Chamadas.Should().HaveCount(8);
    }
}
