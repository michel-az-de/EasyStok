using System.Net;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.TestHelpers.Http;
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
/// <see cref="HttpFalso"/> com payloads no formato da documentação oficial: nenhuma chamada à API real
/// (onda 0.9 ainda sem credencial).
/// </summary>
public class MercadoPagoClientTests
{
    private const string PreferenciaCriadaJson = """{"id":"pref-1","init_point":"https://mp.test/pref-1"}""";

    /// <summary>Responde o mesmo status e corpo a qualquer rota: cada teste confere a rota pela chamada gravada.</summary>
    private static HttpFalso Falso(HttpStatusCode status, string? resposta)
    {
        var handler = new HttpFalso();
        handler.QuandoQualquer().Responde(status, resposta ?? PreferenciaCriadaJson);
        return handler;
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

    private static (MercadoPagoClient, HttpFalso) Criar(HttpStatusCode status = HttpStatusCode.Created, string? resposta = null)
    {
        var handler = Falso(status, resposta);
        var http = handler.CriarClient("https://api.mp.test/");
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
        handler.UltimaChamada!.Cabecalhos["X-Idempotency-Key"].Should().ContainSingle($"{pedidoId:N}-1");
        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
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

        handler.UltimaChamada!.Cabecalhos.ContainsKey("X-Idempotency-Key").Should().BeFalse();
        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
        json.RootElement.GetProperty("expires").GetBoolean().Should().BeFalse();
        json.RootElement.TryGetProperty("expiration_date_to", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("date_of_expiration", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PreferenciaEnviaDateOfExpiration()
    {
        // expiration_date_to fecha o link; o prazo do Pix gerado dentro dele é date_of_expiration.
        // Sem ele, um QR gerado aos 29 min continua pagável depois que o pedido é cancelado.
        var (client, handler) = Criar();
        var expira = new DateTime(2026, 9, 29, 15, 30, 0, DateTimeKind.Utc);

        await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Casa da Babá", 10m, [new PreferenceItemCommand("Item", 1, 10m)],
            ExpiraEm: expira));

        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
        json.RootElement.GetProperty("date_of_expiration").GetString().Should().Be("2026-09-29T15:30:00.000+00:00");
        json.RootElement.GetProperty("expiration_date_to").GetString().Should().Be("2026-09-29T15:30:00.000+00:00");
    }

    [Fact]
    public async Task DataDaPreferenciaVaiComMaisLiteral()
    {
        // #1400: o Mercado Pago responde 400 error_parsing_date quando o + do fuso vem escapado (+), que é o
        // padrão do System.Text.Json. Medido no sandbox: o mesmo JSON com + literal passa. Olha o texto bruto,
        // porque o JsonDocument decodifica o escape e esconderia o defeito.
        var (client, handler) = Criar();

        await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Casa da Babá", 10m, [new PreferenceItemCommand("Entrega — Butantã", 1, 10m)],
            ExpiraEm: new DateTime(2026, 9, 29, 15, 30, 0, DateTimeKind.Utc)));

        handler.UltimaChamada!.Corpo.Should().Contain("\"expiration_date_to\":\"2026-09-29T15:30:00.000+00:00\"")
            .And.Contain("\"date_of_expiration\":\"2026-09-29T15:30:00.000+00:00\"")
            .And.NotContain("\\u002B");
    }

    [Fact]
    public async Task ExpirarPreferenciaMandaDataComMaisLiteral()
    {
        var (client, handler) = Criar(HttpStatusCode.OK);

        await client.ExpirarPreferenciaAsync("pref-1", new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc));

        handler.UltimaChamada!.Corpo.Should().Contain("2026-09-29T15:00:00.000+00:00").And.NotContain("\\u002B");
    }

    [Fact]
    public async Task RecusaDoMercadoPagoLevaOCorpoParaOErro()
    {
        // #1400: o 400 aparecia no log sem o motivo; o corpo do Mercado Pago não traz segredo.
        var (client, _) = Criar(HttpStatusCode.BadRequest,
            """{"message":"error_parsing_date","error":"bad_request","status":400,"cause":null}""");

        var act = () => client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, [new PreferenceItemCommand("Item", 1, 10m)]));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("400").And.Contain("error_parsing_date");
    }

    [Fact]
    public async Task RecusaDoMercadoPagoNaoLevaSegredoParaOErro()
    {
        // I4.5 (#1698): a mensagem da exceção vai para o log e para WebhookRecebido.Erro.
        const string canario = "CANARIO-de-teste";
        var (client, _) = Criar(HttpStatusCode.Unauthorized,
            "{\"message\":\"invalid_token\",\"status\":401,\"access_token\":\"" + canario + "\"}");

        var act = () => client.ConsultarPagamentoAsync("20359978");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("401").And.Contain("invalid_token").And.NotContain(canario);
    }

    [Fact]
    public async Task PreferenceUsaCheckoutPreferences()
    {
        var (client, handler) = Criar();

        await client.CriarPreferenceAsync(new CriarPreferenceCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, [new PreferenceItemCommand("Item", 1, 10m)]));

        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Post);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be("https://api.mp.test/checkout/preferences");
        handler.UltimaChamada.Cabecalho("Authorization").Should().Be("Bearer fake");
    }

    [Fact]
    public async Task EstornoComIdempotencyKey()
    {
        var (client, handler) = Criar(HttpStatusCode.Created,
            """{"id":1009042015,"payment_id":20359978,"amount":10.5,"status":"approved"}""");

        var r = await client.EstornarAsync("20359978", 10.5m, "estorno-chave-1");

        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Post);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978/refunds");
        handler.UltimaChamada.Cabecalhos["X-Idempotency-Key"].Should().ContainSingle("estorno-chave-1");
        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
        json.RootElement.GetProperty("amount").GetDecimal().Should().Be(10.5m);
        r.EstornoId.Should().Be("1009042015");
        r.Valor.Should().Be(10.5m);
        r.Status.Should().Be("approved");
    }

    [Fact]
    public async Task EstornoTotal_VaiSemCorpoEComAChaveDeQuemChamou()
    {
        var (client, handler) = Criar(HttpStatusCode.Created,
            """{"id":77,"payment_id":20359978,"amount":25.5,"status":"approved"}""");

        await client.EstornarAsync("20359978", null, "operacao-9");

        handler.UltimaChamada!.Cabecalhos["X-Idempotency-Key"].Should().ContainSingle("operacao-9");
        handler.UltimaChamada!.Corpo.Should().BeNullOrEmpty("estorno total vai sem amount");
    }

    // ── I3.2 (#1688): a conta recebedora ──

    [Fact]
    public async Task Preferencia_devolve_a_conta_que_vai_receber()
    {
        var (client, _) = Criar(HttpStatusCode.Created, """{"id":"pref-1","init_point":"https://mp.test/pref-1","collector_id":724484980}""");

        var r = await client.CriarPreferenceAsync(new CriarPreferenceCommand(Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, []));

        r.ContaRecebedoraId.Should().Be("724484980");
    }

    [Fact]
    public async Task Pagamento_consultado_traz_a_conta_recebedora()
    {
        var (client, _) = Criar(HttpStatusCode.OK, """{"id":20359978,"status":"approved","transaction_amount":25.5,"collector_id":724484980}""");

        (await client.ConsultarPagamentoAsync("20359978"))!.CollectorId.Should().Be("724484980");
    }

    [Fact]
    public async Task Pagamento_com_a_conta_so_no_objeto_collector_tambem_e_lido()
    {
        var (client, _) = Criar(HttpStatusCode.OK, """{"id":20359978,"status":"approved","transaction_amount":25.5,"collector":{"id":724484980}}""");

        (await client.ConsultarPagamentoAsync("20359978"))!.CollectorId.Should().Be("724484980");
    }

    [Fact]
    public async Task Conta_da_credencial_vem_de_users_me()
    {
        var (client, handler) = Criar(HttpStatusCode.OK, """{"id":724484980,"nickname":"LOJA"}""");

        var conta = await client.ObterContaRecebedoraAsync();

        conta.Should().Be("724484980");
        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Get);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be("https://api.mp.test/users/me");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Credencial_recusada_aparece_com_o_status_ao_consultar_a_conta(HttpStatusCode status)
    {
        var (client, _) = Criar(status, """{"message":"invalid access token"}""");

        var act = () => client.ObterContaRecebedoraAsync();

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    // ── I3.1 (#1667): a chave de idempotência do estorno é de quem chama, nunca derivada do valor ──

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Estorno_sem_chave_de_idempotencia_e_recusado_antes_da_rede(string chave)
    {
        var (client, handler) = Criar(HttpStatusCode.Created, """{"id":77,"amount":10,"status":"approved"}""");

        var act = () => client.EstornarAsync("20359978", 10m, chave);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Dois_parciais_iguais_geram_2_refunds()
    {
        var (client, handler) = Criar(HttpStatusCode.Created,
            """{"id":77,"payment_id":20359978,"amount":10,"status":"approved"}""");

        await client.EstornarAsync("20359978", 10m, "operacao-1");
        await client.EstornarAsync("20359978", 10m, "operacao-2");

        handler.Chamadas.Should().HaveCount(2);
        handler.Chamadas.Select(c => c.Cabecalho("X-Idempotency-Key")).Should().Equal("operacao-1", "operacao-2");
    }

    [Fact]
    public async Task Post_com_timeout_chama_1_vez()
    {
        var handler = new HttpFalso();
        handler.QuandoQualquer().Demora(TimeSpan.FromSeconds(10));
        var http = handler.CriarClient("https://api.mp.test/");
        http.Timeout = TimeSpan.FromMilliseconds(80);
        var client = new MercadoPagoClient(http, Options.Create(new MercadoPagoOptions { AccessToken = "fake" }),
            NullLogger<MercadoPagoClient>.Instance);

        var act = () => client.EstornarAsync("20359978", 10m, "operacao-1");

        await act.Should().ThrowAsync<TaskCanceledException>();
        handler.Chamadas.Should().ContainSingle("escrita no provedor não repete sozinha");
    }

    [Fact]
    public async Task ConsultaPagamentoLeOsCamposDaFonte()
    {
        var (client, handler) = Criar(HttpStatusCode.OK, PagamentoAprovadoJson);

        var p = await client.ConsultarPagamentoAsync("20359978");

        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Get);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978");
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

        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Get);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be(
            "https://api.mp.test/v1/payments/search?external_reference=3f2b8f0e-8a47-4f55-9c3a-5d1f0c1e2a10&sort=date_created&criteria=desc");
        r.Should().ContainSingle().Which.Id.Should().Be("20359978");
    }

    [Fact]
    public async Task ExpirarPreferenciaUsaPutComExpirationDateTo()
    {
        var (client, handler) = Criar(HttpStatusCode.OK, """{"id":"pref-1"}""");

        await client.ExpirarPreferenciaAsync("pref-1", new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc));

        handler.UltimaChamada!.Metodo.Should().Be(HttpMethod.Put);
        handler.UltimaChamada.Uri.AbsoluteUri.Should().Be("https://api.mp.test/checkout/preferences/pref-1");
        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
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
        handler.UltimaChamada.Should().BeNull();
    }

    /// <summary>
    /// A porta resolvida pelo DI precisa ser o client tipado (com <c>BaseAddress</c>); antes o
    /// <c>AddScoped&lt;IMercadoPagoClient, MercadoPagoClient&gt;</c> recebia um <c>HttpClient</c> sem base.
    /// </summary>
    [Fact]
    public async Task ClientRegistradoNoDiUsaBaseUrlConfigurada()
    {
        var handler = Falso(HttpStatusCode.OK, PagamentoAprovadoJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MercadoPago:UseStub"] = "false",
            ["MercadoPago:AccessToken"] = "fake",
            ["MercadoPago:BaseUrl"] = "https://api.mp.test/",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMercadoPagoClient(config, "Development");
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IMercadoPagoClient>();
        await client.ConsultarPagamentoAsync("20359978");

        handler.UltimaChamada!.Uri.AbsoluteUri.Should().Be("https://api.mp.test/v1/payments/20359978");
    }
}
