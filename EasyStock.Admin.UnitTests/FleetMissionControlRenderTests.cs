using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Admin.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// Render do Centro de Comando da Frota (issue 623) via WebApplicationFactory, com a API
/// stubada. Cobre: (1) o shell da frota renderiza, (2) o cockpit por loja (drill-down)
/// nao regrediu, (3) o proxy /api-proxy/admin/operacao/fleet repassa o JSON da API.
/// A board e client-side (Alpine x-text, auto-escapado) — provado estaticamente abaixo.
/// </summary>
public class FleetMissionControlRenderTests : IClassFixture<FleetMissionControlRenderTests.FleetFactory>
{
    private readonly FleetFactory _factory;
    public FleetMissionControlRenderTests(FleetFactory factory) => _factory = factory;

    [Fact]
    public async Task Operacao_sem_empresa_renderiza_o_centro_de_comando()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/Operacao");
        var html = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        html.Should().Contain("operacaoMissionControl");
        html.Should().Contain("Centro de Comando");
        html.Should().Contain("Radar de Risco");

        // Sem double-encoding (guard herdado de EmptyStateEncodingTests p/ esta rota).
        html.Should().NotContain("&amp;lt;");
        html.Should().NotContain("&amp;gt;");

        // A board renderiza nome de tenant via Alpine x-text (auto-escapa) — XSS-safe por
        // construcao. Garante que nao caiu em x-html (sink perigoso).
        html.Should().Contain("x-text=\"t.nome\"");
        html.Should().NotContain("x-html");
    }

    [Fact]
    public async Task Operacao_com_empresa_renderiza_o_cockpit()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync($"/Operacao?empresaId={FleetFactory.EmpresaId}");
        var html = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        html.Should().Contain("Cockpit da loja");
        html.Should().Contain("op-cockpit-grid");
        html.Should().Contain("op-back"); // link "voltar a frota"
    }

    [Fact]
    public async Task Proxy_fleet_repassa_o_json_da_api()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/api-proxy/admin/operacao/fleet");
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Loja Stub");   // nome veio do stub da API (envelope data desempacotado)
        body.Should().Contain("tenants");
    }

    public sealed class FleetFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid EmpresaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();
                services.AddHttpClient<AdminApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new FleetStubHandler());
            });
        }
    }

    private sealed class FakeSuperAdminSession(IHttpContextAccessor accessor) : AdminSessionService(accessor)
    {
        public override string? GetToken()
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"nivel\":\"SuperAdmin\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return "h." + payload + ".s";
        }

        public override string? GetRefreshToken() => null;
    }

    private sealed class FleetStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var fleet = new
            {
                data = new
                {
                    generated = "2026-06-15T12:00:00Z",
                    totalTenants = 1,
                    totals = new
                    {
                        tenantsOnline = 1,
                        vendasHojeTotal = 100m,
                        pedidosTravados = 0,
                        tenantsEmRisco = 0,
                        ticketsSlaViolado = 0,
                        faturasVencidasCount = 0,
                        faturasVencidasValor = 0m,
                        mrrAtivo = 199.9m,
                        suspensos = 0,
                    },
                    tenants = new object[]
                    {
                        new
                        {
                            empresaId = FleetFactory.EmpresaId,
                            nome = "Loja Stub",
                            plano = "Plus",
                            healthScore = 100,
                            healthBand = "ok",
                            vendasHoje = 100m,
                            vendasCount = 2,
                            pedidosAbertos = 0,
                            pedidosTravados = 0,
                            conferenciaPendente = 0,
                            devicesAtivos = 2,
                            devicesTotal = 2,
                            ticketsAbertos = 0,
                            ticketsSlaViolado = 0,
                            faturaVencida = false,
                            trialFim = (string?)null,
                            riscoFlags = Array.Empty<string>(),
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(fleet);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
