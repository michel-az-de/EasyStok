using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Webhook;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.IntegrationTests.Storefront.Webhook;

/// <summary>
/// Testes E2E do <see cref="EasyStock.Api.Controllers.Storefront.WebhookController"/>
/// — recebimento de webhooks MercadoPago (ADR-0006).
///
/// <para>
/// SkippableFact: dependem da API montada (Microsoft.AspNetCore.Mvc.Testing).
/// Habilitar com <c>RUN_API_INTEGRATION=1</c>.
/// </para>
/// </summary>
public sealed class WebhookControllerTests
{
    private const string Secret = "test-webhook-secret-mp-integration-1234";
    private const string WebhookPath = "/api/storefront/webhooks/mercadopago";

    private static string ComputarHmac(string secret, byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static WebApplicationFactory<Program> CriarFactory(
        Action<IServiceCollection>? extraServices = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DatabaseProvider"] = "sqlite",
                        ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                        ["MercadoPago:UseStub"] = "true",
                        ["MercadoPago:WebhookSecret"] = Secret,
                        ["Jwt:Key"] = "test-super-secret-key-min32chars!!",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                    });
                });

                b.ConfigureServices(services =>
                {
                    // Stub IWebhookProcessadoRepository com comportamento configurável por teste.
                    var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IWebhookProcessadoRepository));
                    if (existing is not null) services.Remove(existing);

                    var stubRepo = Substitute.For<IWebhookProcessadoRepository>();
                    stubRepo.TentarRegistrarRecebidoAsync(Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>())
                        .Returns(callInfo =>
                        {
                            var arg = callInfo.Arg<WebhookProcessado>();
                            return Task.FromResult<(bool inserido, WebhookProcessado registro)>((true, arg));
                        });
                    services.AddScoped(_ => stubRepo);

                    extraServices?.Invoke(services);
                });
            });
    }

    // ── HappyPath: 200 OK em < 200ms ──────────────────────────────────────

    [SkippableFact]
    public async Task PostWebhook_HmacValido_Retorna200_EmMenosDe200ms()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();

        var pedidoId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"action\":\"payment.updated\",\"data\":{{\"id\":\"approved-{pedidoId:N}\"}}}}");
        var assinatura = ComputarHmac(Secret, payload);

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = content,
        };
        request.Headers.Add("Authorization", assinatura);
        request.Headers.Add("x-request-id", $"req-{Guid.NewGuid():N}");

        var response = await client.SendAsync(request);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "SLA interno < 200 ms; usamos margem 500 ms para CI lento");
    }

    // ── HMAC inválido → 401 ───────────────────────────────────────────────

    [SkippableFact]
    public async Task PostWebhook_HmacInvalido_Retorna401()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();

        var payload = Encoding.UTF8.GetBytes("{\"action\":\"payment.updated\",\"data\":{\"id\":\"x\"}}");

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = content,
        };
        request.Headers.Add("Authorization", "0000000000000000000000000000000000000000000000000000000000000000");
        request.Headers.Add("x-request-id", $"req-{Guid.NewGuid():N}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Idempotência: 2 POSTs com mesmo x-request-id → 200 ambos ──────────

    [SkippableFact]
    public async Task PostWebhook_MesmoXRequestIdDuasVezes_AmbosRetornam200()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(services =>
        {
            // Re-stub repo: primeira retorna true, segunda retorna false
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IWebhookProcessadoRepository));
            if (existing is not null) services.Remove(existing);

            var stubRepo = Substitute.For<IWebhookProcessadoRepository>();
            var calls = 0;
            stubRepo.TentarRegistrarRecebidoAsync(Arg.Any<WebhookProcessado>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    calls++;
                    var arg = callInfo.Arg<WebhookProcessado>();
                    return Task.FromResult<(bool inserido, WebhookProcessado registro)>(
                        (calls == 1, arg));
                });
            services.AddScoped(_ => stubRepo);
        });

        var client = factory.CreateClient();

        var payload = Encoding.UTF8.GetBytes(
            "{\"action\":\"payment.updated\",\"data\":{\"id\":\"approved-evt\"}}");
        var assinatura = ComputarHmac(Secret, payload);
        var xRequestId = $"req-fixed-{Guid.NewGuid():N}";

        async Task<HttpResponseMessage> EnviarAsync()
        {
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = content };
            request.Headers.Add("Authorization", assinatura);
            request.Headers.Add("x-request-id", xRequestId);
            return await client.SendAsync(request);
        }

        var r1 = await EnviarAsync();
        var r2 = await EnviarAsync();

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
