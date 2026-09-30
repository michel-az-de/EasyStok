using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// S11: a preferência do pedido leva expiração (<c>expires</c> + <c>expiration_date_to</c>) e o header
/// <c>X-Idempotency-Key</c>. Handler HTTP falso: nenhuma chamada à API real (onda 0.9 ainda sem credencial).
/// </summary>
public class MercadoPagoClientTests
{
    private sealed class HandlerFalso : HttpMessageHandler
    {
        public HttpRequestMessage? Requisicao { get; private set; }
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requisicao = request;
            Corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"pref-1","init_point":"https://mp.test/pref-1"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (MercadoPagoClient, HandlerFalso) Criar()
    {
        var handler = new HandlerFalso();
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
}
