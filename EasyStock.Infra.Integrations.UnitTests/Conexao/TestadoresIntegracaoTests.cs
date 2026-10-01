using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Conexao;
using EasyStock.Infra.Integrations.Logistica.Lalamove;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Conexao;

/// <summary>
/// F16 (#1246): teste de conexão de cada provider e o assinador HMAC da Lalamove. HTTP falso
/// (<see cref="HandlerFalso"/>): nenhuma chamada real a provedor, nenhuma chave real.
/// </summary>
public class TestadoresIntegracaoTests
{
    private static ChaveParaTeste Chave(string provider, AmbienteIntegracao ambiente = AmbienteIntegracao.Production,
        params (string Nome, string Valor)[] campos) =>
        new(provider, ambiente, campos.ToDictionary(c => c.Nome, c => c.Valor), OrigemChaveIntegracao.Loja);

    // ─── Assinador da Lalamove ───────────────────────────────────────────────

    [Fact]
    public void AssinaturaLalamoveSegueOFormatoDocumentado()
    {
        // Vetores gerados fora do .NET (openssl dgst -sha256 -hmac) sobre o texto bruto
        // "{ts}\r\n{METODO}\r\n{PATH}\r\n\r\n{BODY}", com a secret do exemplo da documentação.
        AssinadorLalamove.Assinar("sk_test_Lalamove", 1545880607433, "GET", "/v3/cities", "")
            .Should().Be("5f57cf16ff137c2f20c471b438c726853e3f46b38be11faa606e518c2fe163a0");
        AssinadorLalamove.Assinar("sk_test_Lalamove", 1545880607433, "POST", "/v3/quotations", "{\"data\":{}}")
            .Should().Be("a835ef8f07f61c04fb52186f3527b5d91b8a2a1816b4344a5db71888d4794119");
    }

    [Fact]
    public void AutorizacaoLalamoveLevaChaveTimestampEAssinatura()
    {
        AssinadorLalamove.Autorizacao("pk_test_chave", "sk_test_Lalamove", 1545880607433, "GET", "/v3/cities", "")
            .Should().Be("hmac pk_test_chave:1545880607433:5f57cf16ff137c2f20c471b438c726853e3f46b38be11faa606e518c2fe163a0");
    }

    // ─── Lalamove ────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset InstanteLalamove = DateTimeOffset.FromUnixTimeMilliseconds(1545880607433);

    [Fact]
    public async Task LalamoveSandboxConsultaCidadesComAssinatura()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, """{"data":[{"locode":"BR SAO"},{"locode":"BR RIO"}]}""");
        var testador = new TestadorLalamove(new HttpClient(handler), new RelogioFixo(InstanteLalamove));

        var r = await testador.TestarAsync(Chave("lalamove", AmbienteIntegracao.Sandbox,
            ("apiKey", "pk_test_chave"), ("apiSecret", "sk_test_Lalamove")));

        r.Ok.Should().BeTrue();
        r.Mensagem.Should().Contain("2 cidades");
        var req = handler.Requisicoes.Should().ContainSingle().Subject;
        req.Metodo.Should().Be(HttpMethod.Get);
        req.Url.Should().Be("https://rest.sandbox.lalamove.com/v3/cities");
        req.Cabecalhos["Market"].Should().Be("BR");
        req.Cabecalhos["Authorization"].Should().Be(
            "hmac pk_test_chave:1545880607433:5f57cf16ff137c2f20c471b438c726853e3f46b38be11faa606e518c2fe163a0");
        Guid.TryParse(req.Cabecalhos["Request-ID"], out _).Should().BeTrue();
    }

    [Fact]
    public async Task LalamoveChaveErradaDaErroEmPortugues()
    {
        var handler = new HandlerFalso(HttpStatusCode.Unauthorized,
            """{"errors":[{"id":"ERR_UNAUTHORIZED","message":"Unauthorized"}]}""");
        var testador = new TestadorLalamove(new HttpClient(handler), new RelogioFixo(InstanteLalamove));

        var r = await testador.TestarAsync(Chave("lalamove", AmbienteIntegracao.Production,
            ("apiKey", "pk_prod_chave"), ("apiSecret", "sk_prod_errada")));

        r.Ok.Should().BeFalse();
        r.Mensagem.Should().Contain("Lalamove recusou").And.NotContain("sk_prod_errada");
        handler.Requisicoes.Single().Url.Should().StartWith("https://rest.lalamove.com/v3/");
    }

    // ─── Mercado Pago ────────────────────────────────────────────────────────

    [Fact]
    public async Task MercadoPagoConfereOTokenEmUsersMe()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, """{"id":123456,"nickname":"CASADABABA"}""");
        var testador = new TestadorMercadoPago(new HttpClient(handler));

        var r = await testador.TestarAsync(Chave("mercadopago", campos: ("accessToken", "APP_USR-token-teste")));

        r.Ok.Should().BeTrue();
        r.Mensagem.Should().Contain("CASADABABA");
        var req = handler.Requisicoes.Single();
        req.Url.Should().Be("https://api.mercadolibre.com/users/me");
        req.Cabecalhos["Authorization"].Should().Be("Bearer APP_USR-token-teste");
    }

    [Fact]
    public async Task MercadoPagoTokenErradoDaErroEmPortugues()
    {
        var handler = new HandlerFalso(HttpStatusCode.Unauthorized, """{"message":"invalid_token","status":401}""");

        var r = await new TestadorMercadoPago(new HttpClient(handler))
            .TestarAsync(Chave("mercadopago", campos: ("accessToken", "APP_USR-errado")));

        r.Ok.Should().BeFalse();
        r.Mensagem.Should().Contain("Mercado Pago recusou o token").And.NotContain("APP_USR-errado");
    }

    // ─── WhatsApp (Meta) ─────────────────────────────────────────────────────

    private static TestadorWhatsApp WhatsApp(HandlerFalso handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(
            new EasyStock.Infra.Integrations.WhatsApp.WhatsAppCloudOptions { BaseUrl = "https://graph.test/v19.0" }));

    [Fact]
    public async Task WhatsAppConsultaONumeroSemEnviarNada()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK,
            """{"display_phone_number":"+55 11 99999-0000","verified_name":"Casa da Baba","quality_rating":"GREEN","id":"1098765"}""");

        var r = await WhatsApp(handler).TestarAsync(Chave("whatsapp", campos: [("accessToken", "token-meta"), ("phoneNumberId", "1098765")]));

        r.Ok.Should().BeTrue();
        r.Mensagem.Should().Contain("+55 11 99999-0000").And.Contain("Casa da Baba");
        var req = handler.Requisicoes.Single();
        req.Metodo.Should().Be(HttpMethod.Get, "o teste não pode mandar mensagem a ninguém");
        req.Url.Should().Be("https://graph.test/v19.0/1098765?fields=display_phone_number,verified_name,quality_rating");
        req.Cabecalhos["Authorization"].Should().Be("Bearer token-meta");
    }

    [Fact]
    public async Task WhatsAppTokenVencidoDaErroEmPortugues()
    {
        var handler = new HandlerFalso(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Error validating access token: Session has expired","type":"OAuthException","code":190}}""");

        var r = await WhatsApp(handler).TestarAsync(Chave("whatsapp", campos: [("accessToken", "token-velho"), ("phoneNumberId", "1098765")]));

        r.Ok.Should().BeFalse();
        r.Mensagem.Should().Contain("token da Meta").And.Contain("190");
    }

    [Fact]
    public async Task WhatsAppNumeroForaDoFormatoNemChamaAMeta()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, "{}");

        var r = await WhatsApp(handler).TestarAsync(Chave("whatsapp", campos: [("accessToken", "t"), ("phoneNumberId", "../me/messages")]));

        r.Ok.Should().BeFalse();
        handler.Requisicoes.Should().BeEmpty();
    }

    // ─── Google Maps ─────────────────────────────────────────────────────────

    private static TestadorGoogleMaps Maps(HandlerFalso handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://maps.test/") }, NullLoggerFactory.Instance);

    [Fact]
    public async Task GoogleMapsChaveBoaPassa()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, """{"status":"OK","results":[]}""");

        var r = await Maps(handler).TestarAsync(Chave("googlemaps", campos: ("apiKey", "AIza-chave-boa")));

        r.Ok.Should().BeTrue();
        handler.Requisicoes.Single().Url.Should().StartWith("https://maps.test/geocode/json?");
    }

    [Fact]
    public async Task GoogleMapsChaveRecusadaDaErroEmPortugues()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, """{"status":"REQUEST_DENIED","error_message":"The provided API key is invalid."}""");

        var r = await Maps(handler).TestarAsync(Chave("googlemaps", campos: ("apiKey", "AIza-chave-ruim")));

        r.Ok.Should().BeFalse();
        r.Mensagem.Should().Contain("Google recusou a chave").And.NotContain("AIza-chave-ruim");
    }

    [Fact]
    public async Task GoogleMapsCotaEstouradaDaErroEmPortugues()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, """{"status":"OVER_QUERY_LIMIT"}""");

        var r = await Maps(handler).TestarAsync(Chave("googlemaps", campos: ("apiKey", "AIza-chave")));

        r.Ok.Should().BeFalse();
        r.Mensagem.Should().Contain("cota");
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    // ─── HTTP falso ──────────────────────────────────────────────────────────

    private sealed record Requisicao(HttpMethod Metodo, string Url, Dictionary<string, string> Cabecalhos, string? Corpo);

    private sealed class HandlerFalso(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public List<Requisicao> Requisicoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cabecalhos = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var conteudo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requisicoes.Add(new Requisicao(request.Method, request.RequestUri!.ToString(), cabecalhos, conteudo));
            return new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        }
    }
}
