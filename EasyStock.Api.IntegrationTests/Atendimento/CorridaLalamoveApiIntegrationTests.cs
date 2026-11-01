using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Integrations.Logistica.Lalamove;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2160 ponta a ponta: API, Postgres, controllers e DI reais; só a Lalamove é um HTTP falso com o formato da v3.
/// Cota, chama, e os webhooks assinados levam a corrida até entregue, movendo viagem e pedido.
/// </summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Corrida_lalamove_cotar_chamar_e_webhooks_ate_entregue()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var lalamove = new LalamoveFalsa();
        await using var factory = CriarFactoryLalamove(lalamove);
        using var client = factory.CreateClient();
        var (viagemId, pedidoId) = await SemearCorridaAsync(factory);

        var cotacao = await Dados(await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/cotacao", new { veiculo = "Moto" }));
        cotacao.GetProperty("valorCentavos").GetInt32().Should().Be(1740);
        var cotacaoId = cotacao.GetProperty("cotacaoId").GetString();

        var chamada = await Dados(await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/chamar", new { cotacaoId, veiculo = "Moto" }));
        chamada.GetProperty("status").GetString().Should().Be("ProcurandoMotorista");
        var corridaId = chamada.GetProperty("id").GetGuid();
        lalamove.Metadata.Should().Be(corridaId.ToString(), "R4: o id da corrida vai no metadata");

        var segunda = await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/chamar", new { cotacaoId, veiculo = "Moto" });
        segunda.StatusCode.Should().Be(HttpStatusCode.BadRequest, "uma corrida em curso por viagem");

        lalamove.Mudar("ON_GOING", motorista: "80001");
        (await client.PostAsync("/api/webhooks/lalamove", Webhook("e1", lalamove.OrderId))).StatusCode.Should().Be(HttpStatusCode.OK);
        var aCaminho = await Dados(await client.GetAsync($"/api/atendimento/viagens/{viagemId}/corridas/{corridaId}"));
        aCaminho.GetProperty("status").GetString().Should().Be("MotoristaACaminho");
        aCaminho.GetProperty("motorista").GetString().Should().Be("Carlos");

        lalamove.Mudar("PICKED_UP");
        await client.PostAsync("/api/webhooks/lalamove", Webhook("e2", lalamove.OrderId));
        (await StatusPedidoAsync(factory, pedidoId)).Should().Be(StatusPedido.SaiuParaEntrega);

        // Webhook com assinatura de outra chave não muda nada, mas responde 200 (a Lalamove desativa a URL com falhas).
        lalamove.Mudar("COMPLETED", total: "19.00");
        (await client.PostAsync("/api/webhooks/lalamove", Webhook("e3", lalamove.OrderId, segredo: "outro"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusPedidoAsync(factory, pedidoId)).Should().Be(StatusPedido.SaiuParaEntrega);

        await client.PostAsync("/api/webhooks/lalamove", Webhook("e4", lalamove.OrderId));
        (await StatusPedidoAsync(factory, pedidoId)).Should().Be(StatusPedido.Entregue);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var corrida = await db.CorridasExternas.SingleAsync(c => c.Id == corridaId);
        corrida.Status.Should().Be(StatusCorrida.Entregue);
        corrida.CustoCentavos.Should().Be(1900, "o preço final da Lalamove substitui o cotado");
        (await db.Viagens.SingleAsync(v => v.Id == viagemId)).Situacao.Should().Be(SituacaoViagem.Concluida);
    }

    [SkippableFact]
    public async Task Corrida_lalamove_cancelada_procurando_motorista_deixa_a_viagem_para_chamar_de_novo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var lalamove = new LalamoveFalsa();
        await using var factory = CriarFactoryLalamove(lalamove);
        using var client = factory.CreateClient();
        var (viagemId, pedidoId) = await SemearCorridaAsync(factory);
        var cotacaoId = (await Dados(await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/cotacao", new { })))
            .GetProperty("cotacaoId").GetString();
        var corridaId = (await Dados(await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/chamar", new { cotacaoId })))
            .GetProperty("id").GetGuid();

        var cancelada = await Dados(await client.PostAsync($"/api/atendimento/viagens/{viagemId}/corridas/{corridaId}/cancelar", null));

        cancelada.GetProperty("status").GetString().Should().Be("Cancelada");
        cancelada.GetProperty("custoCentavos").GetInt32().Should().Be(0);
        lalamove.Cancelado.Should().BeTrue();
        (await StatusPedidoAsync(factory, pedidoId)).Should().Be(StatusPedido.Pronto);
        var nova = await client.PostAsJsonAsync($"/api/atendimento/viagens/{viagemId}/corridas/chamar", new { cotacaoId });
        nova.StatusCode.Should().Be(HttpStatusCode.OK, "a viagem ficou montando e aceita outra corrida");
    }

    private WebApplicationFactory<Program> CriarFactoryLalamove(LalamoveFalsa lalamove) =>
        CriarFactory(nivel: NivelAcesso.Admin).WithWebHostBuilder(b =>
        {
            b.UseSetting("BackgroundJobs:EnableCorridasLalamove", "false");
            b.ConfigureTestServices(s =>
            {
                var credenciais = Substitute.For<IIntegrationCredentialResolver>();
                credenciais.ObterAsync<Dictionary<string, string>>(Arg.Any<Guid>(), "lalamove", AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
                    .Returns(new Dictionary<string, string> { ["apiKey"] = "pk_prod", ["apiSecret"] = "sk_prod" });
                var fabrica = Substitute.For<IHttpClientFactory>();
                fabrica.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(lalamove, disposeHandler: false) { BaseAddress = new Uri("https://rest.lalamove.com") });
                s.RemoveAll<ILogisticaGateway>();
                s.AddScoped<ILogisticaGateway>(_ => new LalamoveLogisticaGateway(fabrica, credenciais,
                    new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<LalamoveLogisticaGateway>.Instance));
            });
        });

    private static async Task<(Guid ViagemId, Guid PedidoId)> SemearCorridaAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        if (!await db.Empresas.AnyAsync(e => e.Id == EmpresaId))
            db.Empresas.Add(new Empresa { Id = EmpresaId, Nome = "Casa da Baba", Documento = EmpresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        if (!await db.Storefronts.AnyAsync(s => s.EmpresaId == EmpresaId))
        {
            var loja = StorefrontEntity.Criar(EmpresaId, $"lala-{EmpresaId:N}"[..30], "Casa da Baba", 0m);
            loja.ConfigurarFreteRaio(-23.5613, -46.6565, 1.3, 0, 8000, null);
            loja.AtualizarBranding(whatsappPedidos: "11988887777");
            db.Storefronts.Add(loja);
        }
        var pedido = Pedido.Criar(EmpresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        pedido.ClienteNome = "Ana";
        pedido.ClienteTelefone = "11999990000";
        pedido.EnderecoEntrega = "Rua X, 100";
        pedido.EntregaLat = -23.5874;
        pedido.EntregaLng = -46.6576;
        db.Pedidos.Add(pedido);
        var viagem = Viagem.Criar(EmpresaId, DateTime.UtcNow);
        viagem.IncluirParada(pedido.Id, clienteBloqueado: false);
        db.Viagens.Add(viagem);
        await db.SaveChangesAsync();
        return (viagem.Id, pedido.Id);
    }

    private static async Task<StatusPedido> StatusPedidoAsync(WebApplicationFactory<Program> factory, Guid pedidoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        return (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).StatusEnum;
    }

    private static async Task<JsonElement> Dados(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement.GetProperty("data").Clone();
    }

    private static StringContent Webhook(string eventId, string orderId, string segredo = "sk_prod")
    {
        var data = "{\"order\":{\"orderId\":\"" + orderId + "\"}}";
        const long timestamp = 1761930000;
        var bruto = $"{timestamp}\r\nPOST\r\n{LalamoveLogisticaGateway.CaminhoWebhook}\r\n\r\n{data}";
        var assinatura = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(bruto)));
        var corpo = "{\"apiKey\":\"pk_prod\",\"timestamp\":" + timestamp + ",\"signature\":\"" + assinatura + "\",\"eventId\":\"" + eventId
            + "\",\"eventType\":\"ORDER_STATUS_CHANGED\",\"eventVersion\":\"v3\",\"data\":" + data + "}";
        return new StringContent(corpo, Encoding.UTF8, "application/json");
    }

    /// <summary>Lalamove v3 em memória: cotação, pedido, consulta, motorista e cancelamento.</summary>
    private sealed class LalamoveFalsa : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> _estado = new();
        public string OrderId { get; private set; } = "";
        public string? Metadata { get; private set; }
        public bool Cancelado { get; private set; }

        public void Mudar(string status, string? motorista = null, string? total = null)
        {
            _estado["status"] = status;
            if (motorista is not null) _estado["driver"] = motorista;
            if (total is not null) _estado["total"] = total;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var caminho = request.RequestUri!.AbsolutePath;
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (request.Method == HttpMethod.Get && caminho == "/v3/cities")
                return Ok("""{"data":[{"locode":"BR_SAO","services":[{"key":"LALAGO","description":"Moto"},{"key":"CAR","description":"Carro"}]}]}""");
            if (caminho.StartsWith("/v3/quotations", StringComparison.Ordinal))
                return Ok(Cotacao.Replace("__EXPIRA__", DateTimeOffset.UtcNow.AddMinutes(5).ToString("O")));
            if (request.Method == HttpMethod.Post && caminho == "/v3/orders")
            {
                using var doc = JsonDocument.Parse(corpo);
                Metadata = doc.RootElement.GetProperty("data").GetProperty("metadata").GetProperty("corridaId").GetString();
                OrderId = "33" + Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999);
                _estado.Clear();
                Mudar("ASSIGNING_DRIVER");
                return Ok(Pedido());
            }
            if (request.Method == HttpMethod.Get && caminho.Contains("/drivers/", StringComparison.Ordinal))
                return Ok("""{"data":{"driverId":"80001","name":"Carlos","phone":"+5511977776666","plateNumber":"XYZ9A88"}}""");
            if (request.Method == HttpMethod.Get && caminho == "/v3/orders/" + OrderId)
                return Ok(Pedido());
            if (request.Method == HttpMethod.Delete && caminho == "/v3/orders/" + OrderId)
            {
                Cancelado = true;
                Mudar("CANCELED");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"errors":[{"id":"ERR_NOT_FOUND"}]}""") };
        }

        private string Pedido() => PedidoJson
            .Replace("__ID__", OrderId).Replace("__STATUS__", _estado["status"])
            .Replace("__DRIVER__", _estado.GetValueOrDefault("driver", "")).Replace("__TOTAL__", _estado.GetValueOrDefault("total", "17.40"))
            .Replace("__CORRIDA__", Metadata);

        private const string Cotacao = """
            {"data":{"quotationId":"q-1","expiresAt":"__EXPIRA__","isRouteOptimized":false,
              "stops":[{"stopId":"s1","coordinates":{"lat":"-23.5613","lng":"-46.6565"},"address":"Casa da Baba"},
                       {"stopId":"s2","coordinates":{"lat":"-23.5874","lng":"-46.6576"},"address":"Rua X"}],
              "priceBreakdown":{"total":"17.40","currency":"BRL"},"distance":{"value":"4120","unit":"m"}}}
            """;

        private const string PedidoJson = """
            {"data":{"orderId":"__ID__","quotationId":"q-1","status":"__STATUS__","shareLink":"https://share.lalamove.com/__ID__",
              "driverId":"__DRIVER__","priceBreakdown":{"total":"__TOTAL__","currency":"BRL"},"metadata":{"corridaId":"__CORRIDA__"}}}
            """;

        private static HttpResponseMessage Ok(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
