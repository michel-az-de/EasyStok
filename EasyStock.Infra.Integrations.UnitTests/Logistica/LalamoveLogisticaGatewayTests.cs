using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Logistica.Lalamove;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Logistica;

/// <summary>Despacho pela API v3 da Lalamove (#2160): HTTP falso com o formato da v3, sem rede.</summary>
public class LalamoveLogisticaGatewayTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.FromUnixTimeMilliseconds(1761930000000);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Corrida = Guid.NewGuid();

    private const string Pedido = """
        {"data":{"orderId":"3300000000000001","quotationId":"q1","priceBreakdown":{"total":"17.40","currency":"BRL"},
          "driverId":"80001","shareLink":"https://share.lalamove.com/abc","status":"ON_GOING","distance":{"value":"4120","unit":"m"},
          "metadata":{"corridaId":"__CORRIDA__"}}}
        """;

    private readonly HttpFalso _producao = new();
    private readonly HttpFalso _sandbox = new();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IHttpClientFactory _fabrica = Substitute.For<IHttpClientFactory>();

    public LalamoveLogisticaGatewayTests()
    {
        _fabrica.CreateClient(LalamoveLogisticaGateway.ClienteProducao).Returns(_ => _producao.CriarClient("https://rest.lalamove.com"));
        _fabrica.CreateClient(LalamoveLogisticaGateway.ClienteSandbox).Returns(_ => _sandbox.CriarClient("https://rest.sandbox.lalamove.com"));
        Chave(AmbienteIntegracao.Production, "pk_prod", "sk_prod");
    }

    private void Chave(AmbienteIntegracao ambiente, string apiKey, string secret) =>
        _credenciais.ObterAsync<Dictionary<string, string>>(Empresa, "lalamove", ambiente, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["apiKey"] = apiKey, ["apiSecret"] = secret });

    private void SemChave(AmbienteIntegracao ambiente) =>
        _credenciais.ObterAsync<Dictionary<string, string>>(Empresa, "lalamove", ambiente, Arg.Any<CancellationToken>())
            .Returns((Dictionary<string, string>?)null);

    private LalamoveLogisticaGateway Sut() => new(_fabrica, _credenciais, new MemoryCache(new MemoryCacheOptions()),
        new RelogioFixo(Agora), NullLogger<LalamoveLogisticaGateway>.Instance);

    private static PedidoChamadaCorrida Chamada() => new(
        new CotacaoCorrida("q1", 1740, Agora.AddMinutes(5), 4120, "s1", "s2"),
        new ContatoCorrida("Casa da Baba", "11988887777"), new ContatoCorrida("Ana", "(11) 99999-0000"), "Portão azul", Corrida);

    [Fact]
    public async Task Chamar_manda_id_da_corrida_no_metadata_e_telefones_em_e164()
    {
        _producao.Quando(HttpMethod.Post, "/v3/orders").Responde(HttpStatusCode.Created, Pedido.Replace("__CORRIDA__", Corrida.ToString()));

        var situacao = await Sut().ChamarAsync(Empresa, Chamada());

        situacao.Should().Be(new SituacaoCorridaProvedor("3300000000000001", StatusCorrida.MotoristaACaminho,
            "https://share.lalamove.com/abc", 1740, "80001", Corrida));
        using var corpo = JsonDocument.Parse(_producao.Chamadas.Single().Corpo!);
        var data = corpo.RootElement.GetProperty("data");
        data.GetProperty("metadata").GetProperty("corridaId").GetString().Should().Be(Corrida.ToString());
        data.GetProperty("sender").GetProperty("phone").GetString().Should().Be("+5511988887777");
        var destinatario = data.GetProperty("recipients")[0];
        destinatario.GetProperty("phone").GetString().Should().Be("+5511999990000");
        destinatario.GetProperty("stopId").GetString().Should().Be("s2");
        destinatario.GetProperty("name").GetString().Should().Be("Ana Portão azul");
    }

    [Fact]
    public async Task So_sandbox_conectado_usa_o_host_do_sandbox()
    {
        SemChave(AmbienteIntegracao.Production);
        Chave(AmbienteIntegracao.Sandbox, "pk_test_x", "sk_test_x");
        _sandbox.Quando(HttpMethod.Get, "/v3/orders/3300000000000001").Responde(HttpStatusCode.OK, Pedido.Replace("__CORRIDA__", "x"));

        var situacao = await Sut().ConsultarAsync(Empresa, "3300000000000001");

        situacao!.Status.Should().Be(StatusCorrida.MotoristaACaminho);
        situacao.CorridaId.Should().BeNull("metadata que não é Guid não vale");
        _producao.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Chave_de_teste_gravada_como_producao_nao_conta()
    {
        Chave(AmbienteIntegracao.Production, "pk_test_errada", "x");
        SemChave(AmbienteIntegracao.Sandbox);

        (await Sut().ConectadoAsync(Empresa)).Should().BeFalse();
        var act = () => Sut().ConsultarAsync(Empresa, "1");
        await act.Should().ThrowAsync<LogisticaNaoConectadaException>();
    }

    [Fact]
    public async Task Pedido_que_a_lalamove_nao_conhece_volta_nulo()
    {
        _producao.Quando(HttpMethod.Get, "/v3/orders/999").Responde(HttpStatusCode.NotFound, """{"errors":[{"id":"ERR_INVALID_ORDER_ID"}]}""");

        (await Sut().ConsultarAsync(Empresa, "999")).Should().BeNull();
    }

    [Fact]
    public async Task Sem_saldo_vira_recusa_com_mensagem_para_o_operador()
    {
        _producao.Quando(HttpMethod.Post, "/v3/orders").Responde(HttpStatusCode.PaymentRequired, """{"errors":[{"id":"ERR_INSUFFICIENT_CREDIT"}]}""");

        var act = () => Sut().ChamarAsync(Empresa, Chamada());

        (await act.Should().ThrowAsync<LogisticaRecusadaException>()).Which.Message.Should().Contain("sem saldo");
    }

    [Fact]
    public async Task Timeout_vira_indisponivel_sem_repetir_o_post()
    {
        _producao.Quando(HttpMethod.Post, "/v3/orders").Lanca(new TaskCanceledException("timeout"));

        var act = () => Sut().ChamarAsync(Empresa, Chamada());

        await act.Should().ThrowAsync<LogisticaIndisponivelException>();
        _producao.Chamadas.Should().ContainSingle("R3: o POST de criação nunca é repetido");
    }

    [Fact]
    public async Task Cotacao_de_carro_escolhe_o_servico_de_carro_e_devolve_os_ids_das_paradas()
    {
        _producao.Quando(HttpMethod.Get, "/v3/cities").Responde(HttpStatusCode.OK,
            """{"data":[{"locode":"BR_SAO","services":[{"key":"LALAGO","description":"Moto"},{"key":"CAR","description":"Carro"}]}]}""");
        _producao.Quando(HttpMethod.Post, "/v3/quotations").Responde(HttpStatusCode.Created, """
            {"data":{"quotationId":"q9","expiresAt":"2025-10-31T17:05:00Z","isRouteOptimized":false,
              "stops":[{"stopId":"a","coordinates":{"lat":"-23.5","lng":"-46.6"},"address":"Loja"},
                       {"stopId":"b","coordinates":{"lat":"-23.6","lng":"-46.7"},"address":"Cliente"}],
              "priceBreakdown":{"total":"31.00","currency":"BRL"},"distance":{"value":"5000","unit":"m"}}}
            """);

        var cotacao = await Sut().CotarAsync(Empresa, new PedidoCotacaoCorrida(
            new PontoCorrida(-23.5, -46.6, "Loja"), new PontoCorrida(-23.6, -46.7, "Cliente"), VeiculoCorrida.Carro, null));

        cotacao.Should().Be(new CotacaoCorrida("q9", 3100, DateTimeOffset.Parse("2025-10-31T17:05:00Z", CultureInfo.InvariantCulture), 5000, "a", "b"));
        using var corpo = JsonDocument.Parse(_producao.Chamadas.Single(c => c.Metodo == HttpMethod.Post).Corpo!);
        corpo.RootElement.GetProperty("data").GetProperty("serviceType").GetString().Should().Be("CAR");
    }

    [Fact]
    public async Task Gorjeta_vai_em_reais_com_duas_casas()
    {
        _producao.Quando(HttpMethod.Post, "/v3/orders/1/priority-fee").Responde(HttpStatusCode.OK, Pedido.Replace("__CORRIDA__", "x"));

        await Sut().AdicionarGorjetaAsync(Empresa, "1", 550);

        using var corpo = JsonDocument.Parse(_producao.Chamadas.Single().Corpo!);
        corpo.RootElement.GetProperty("data").GetProperty("priorityFee").GetString().Should().Be("5.50");
    }

    [Theory]
    [InlineData("ASSIGNING_DRIVER", StatusCorrida.ProcurandoMotorista)]
    [InlineData("ON_GOING", StatusCorrida.MotoristaACaminho)]
    [InlineData("PICKED_UP", StatusCorrida.Coletada)]
    [InlineData("COMPLETED", StatusCorrida.Entregue)]
    [InlineData("CANCELED", StatusCorrida.Cancelada)]
    [InlineData("REJECTED", StatusCorrida.Rejeitada)]
    [InlineData("EXPIRED", StatusCorrida.Expirada)]
    public void Mapa_de_status_da_v3(string status, StatusCorrida esperado) =>
        LalamoveLogisticaGateway.MapearStatus(status).Should().Be(esperado);

    [Fact]
    public async Task Webhook_com_assinatura_da_loja_confere_e_com_outra_chave_nao()
    {
        const string data = """{"order":{"orderId":"3300000000000001","status":"PICKED_UP"}}""";
        var bruto = $"1761930000\r\nPOST\r\n{LalamoveLogisticaGateway.CaminhoWebhook}\r\n\r\n{data}";
        var assinatura = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("sk_prod"), Encoding.UTF8.GetBytes(bruto)));
        string Corpo(string apiKey, string sig) =>
            $$"""{"apiKey":"{{apiKey}}","timestamp":1761930000,"signature":"{{sig}}","eventId":"e1","eventType":"ORDER_STATUS_CHANGED","data":{{data}}}""";

        (await Sut().WebhookAutenticoAsync(Empresa, Corpo("pk_prod", assinatura))).Should().BeTrue();
        (await Sut().WebhookAutenticoAsync(Empresa, Corpo("pk_outra", assinatura))).Should().BeFalse();
        (await Sut().WebhookAutenticoAsync(Empresa, Corpo("pk_prod", new string('0', 64)))).Should().BeFalse();
        (await Sut().WebhookAutenticoAsync(Empresa, "não é json")).Should().BeFalse();
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
