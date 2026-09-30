using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// S11: a preferência do pedido leva expiração (<c>expires</c> + <c>expiration_date_to</c>) e o header
/// <c>X-Idempotency-Key</c>. S32: endpoint do Checkout Pro (<c>checkout/preferences</c>), consulta do
/// pagamento na fonte, busca por <c>external_reference</c>, estorno idempotente e expiração da preferência.
/// Handler HTTP falso com payloads no formato da documentação oficial: nenhuma chamada à API real
/// (onda 0.9 ainda sem credencial).
/// </summary>
public class MercadoPagoClientTests
{
    internal sealed class HandlerFalso(HttpStatusCode status = HttpStatusCode.Created, string? resposta = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Requisicao { get; private set; }
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requisicao = request;
            Corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    resposta ?? """{"id":"pref-1","init_point":"https://mp.test/pref-1"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Pagamento aprovado, recortado do exemplo de <c>GET /v1/payments/{id}</c> da documentação.</summary>
    internal const string PagamentoAprovadoJson = """
        {
          "id": 20359978,
          "date_created": "2026-09-29T11:47:58.000-04:00",
          "date_approved": "2026-09-29T11:48:10.000-04:00",
          "status": "approved",
          "status_detail": "accredited",
          "payment_method_id": "visa",
          "payment_type_id": "credit_card",
          "external_reference": "3f2b8f0e-8a47-4f55-9c3a-5d1f0c1e2a10",
          "transaction_amount": 25.5,
          "currency_id": "BRL"
        }
        """;

    private static (MercadoPagoClient, HandlerFalso) Criar(HttpStatusCode status = HttpStatusCode.Created, string? resposta = null)
    {
        var handler = new HandlerFalso(status, resposta);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mp.test/") };
        var opcoes = Options.Create(new MercadoPagoOptions { AccessToken = "fake" });
        return (new MercadoPagoClient(http, opcoes, NullLogger<MercadoPagoClient>.Instance), handler);
    }

    [Fact]
    public async Task PreferenciaComExpiracaoEIdempotencyKey()
    {
        var (client, handler) = Criar();
        var pedidoId = Guid.NewGuid();
        var expira = new DateTime(2026, 9, 29, 15, 30, 0, DateTimeKind.Utc);

        var r = await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            pedidoId, Guid.NewGuid(), "Casa da Babá", 25m,
            [new PreferenceItemCommand("Brigadeiro", 2, 10m), new PreferenceItemCommand("Frete", 1, 5m)],
            ExpiraEm: expira, IdempotencyKey: $"{pedidoId:N}-1"));

        r.InitPointUrl.Should().Be("https://mp.test/pref-1");
        handler.Requisicao!.Headers.GetValues("X-Idempotency-Key").Should().ContainSingle($"{pedidoId:N}-1");
        using var json = JsonDocument.Parse(handler.Corpo!);
        json.RootElement.GetProperty("external_reference").GetString().Should().Be(pedidoId.ToString());
        json.RootElement.GetProperty("expires").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("expiration_date_to").GetString().Should().Be("2026-09-29T15:30:00.000+00:00");
        json.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task SemExpiracao_NaoMandaDataNemHeader()
    {
        var (client, handler) = Criar();

        await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, [new PreferenceItemCommand("Item", 1, 10m)]));

        handler.Requisicao!.Headers.Contains("X-Idempotency-Key").Should().BeFalse();
        using var json = JsonDocument.Parse(handler.Corpo!);
        json.RootElement.GetProperty("expires").GetBoolean().Should().BeFalse();
        json.RootElement.TryGetProperty("expiration_date_to", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PreferenceUsaCheckoutPreferences()
    {
        var (client, handler) = Criar();

        await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, [new PreferenceItemCommand("Item", 1, 10m)]));

        handler.Requisicao!.Method.Should().Be(HttpMethod.Post);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://api.mp.test/checkout/preferences");
        handler.Requisicao.Headers.Authorization!.ToString().Should().Be("Bearer fake");
    }

    [Fact]
    public async Task EstornoComIdempotencyKey()
    {
        var (client, handler) = Criar(HttpStatusCode.Created,
            """{"id":1009042015,"payment_id":20359978,"amount":10.5,"status":"approved"}""");

        var r = await client.EstornarAsync("20359978", 10.5m, "estorno-chave-1");

        handler.Requisicao!.Method.Should().Be(HttpMethod.Post);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978/refunds");
        handler.Requisicao.Headers.GetValues("X-Idempotency-Key").Should().ContainSingle("estorno-chave-1");
        using var json = JsonDocument.Parse(handler.Corpo!);
        json.RootElement.GetProperty("amount").GetDecimal().Should().Be(10.5m);
        r.EstornoId.Should().Be("1009042015");
        r.Valor.Should().Be(10.5m);
        r.Status.Should().Be("approved");
    }

    [Fact]
    public async Task EstornoTotalSemChave_UsaChaveDeterministicaESemCorpo()
    {
        var (client, handler) = Criar(HttpStatusCode.Created,
            """{"id":77,"payment_id":20359978,"amount":25.5,"status":"approved"}""");

        await client.EstornarAsync("20359978");

        handler.Requisicao!.Headers.GetValues("X-Idempotency-Key").Should().ContainSingle("estorno-20359978-total");
        handler.Corpo.Should().BeNullOrEmpty("estorno total vai sem amount");
    }

    [Fact]
    public async Task ConsultaPagamentoLeOsCamposDaFonte()
    {
        var (client, handler) = Criar(HttpStatusCode.OK, PagamentoAprovadoJson);

        var p = await client.ConsultarPagamentoAsync("20359978");

        handler.Requisicao!.Method.Should().Be(HttpMethod.Get);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978");
        p.Should().NotBeNull();
        p!.Id.Should().Be("20359978");
        p.Aprovado.Should().BeTrue();
        p.StatusDetail.Should().Be("accredited");
        p.ExternalReference.Should().Be("3f2b8f0e-8a47-4f55-9c3a-5d1f0c1e2a10");
        p.TransactionAmount.Should().Be(25.5m);
        p.DateApproved.Should().Be(new DateTime(2026, 9, 29, 15, 48, 10, DateTimeKind.Utc));
        p.DateApproved!.Value.Kind.Should().Be(DateTimeKind.Utc);
        p.PaymentMethodId.Should().Be("visa");
        p.PaymentTypeId.Should().Be("credit_card");
    }

    [Fact]
    public async Task ConsultaPagamentoInexistente_DevolveNull()
    {
        var (client, _) = Criar(HttpStatusCode.NotFound, """{"message":"Payment not found","status":404}""");

        var p = await client.ConsultarPagamentoAsync("1");

        p.Should().BeNull();
    }

    [Fact]
    public async Task ConsultaPagamentoComFalhaDoProvedor_Lanca()
    {
        var (client, _) = Criar(HttpStatusCode.InternalServerError, "{}");

        var act = () => client.ConsultarPagamentoAsync("1");

        await act.Should().ThrowAsync<HttpRequestException>("o controller responde 500 e o Mercado Pago reenvia");
    }

    [Fact]
    public async Task BuscaPorReferencia_UsaPaymentsSearchDoMaisNovo()
    {
        var (client, handler) = Criar(HttpStatusCode.OK,
            $$"""{"paging":{"total":1,"limit":30,"offset":0},"results":[{{PagamentoAprovadoJson}}]}""");

        var r = await client.BuscarPagamentosPorReferenciaAsync("3f2b8f0e-8a47-4f55-9c3a-5d1f0c1e2a10");

        handler.Requisicao!.Method.Should().Be(HttpMethod.Get);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be(
            "https://api.mp.test/v1/payments/search?external_reference=3f2b8f0e-8a47-4f55-9c3a-5d1f0c1e2a10&sort=date_created&criteria=desc");
        r.Should().ContainSingle().Which.Id.Should().Be("20359978");
    }

    [Fact]
    public async Task ExpirarPreferenciaUsaPutComExpirationDateTo()
    {
        var (client, handler) = Criar(HttpStatusCode.OK, """{"id":"pref-1"}""");

        await client.ExpirarPreferenciaAsync("pref-1", new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc));

        handler.Requisicao!.Method.Should().Be(HttpMethod.Put);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://api.mp.test/checkout/preferences/pref-1");
        using var json = JsonDocument.Parse(handler.Corpo!);
        json.RootElement.GetProperty("expires").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("expiration_date_to").GetString().Should().Be("2026-09-29T15:00:00.000+00:00");
    }

    [Theory]
    [InlineData("../users/me")]
    [InlineData("123?x=1")]
    [InlineData("")]
    public async Task IdForaDoFormato_NaoChamaAApi(string id)
    {
        var (client, handler) = Criar(HttpStatusCode.OK, PagamentoAprovadoJson);

        var act = () => client.ConsultarPagamentoAsync(id);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requisicao.Should().BeNull();
    }

    /// <summary>
    /// A porta resolvida pelo DI precisa ser o client tipado (com <c>BaseAddress</c>); antes o
    /// <c>AddScoped&lt;IMercadoPagoClient, MercadoPagoClient&gt;</c> recebia um <c>HttpClient</c> sem base.
    /// </summary>
    [Fact]
    public async Task ClientRegistradoNoDiUsaBaseUrlConfigurada()
    {
        var handler = new HandlerFalso(HttpStatusCode.OK, PagamentoAprovadoJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MercadoPago:UseStub"] = "false",
            ["MercadoPago:AccessToken"] = "fake",
            ["MercadoPago:BaseUrl"] = "https://api.mp.test/",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMercadoPagoClient(config);
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IMercadoPagoClient>();
        await client.ConsultarPagamentoAsync("20359978");

        handler.Requisicao!.RequestUri!.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978");
    }
}
