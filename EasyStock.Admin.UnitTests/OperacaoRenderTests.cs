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
/// Render da tela Operação (issue 623, reescrita clara) via WebApplicationFactory, com a API
/// stubada. Cobre: (1) a página renderiza em linguagem clara (sem o jargão antigo), e
/// (2) o proxy /api-proxy/admin/operacao/fleet repassa o JSON da API. A lista é client-side
/// (Alpine x-text, auto-escapado) — provado estaticamente abaixo.
/// </summary>
public class OperacaoRenderTests : IClassFixture<OperacaoRenderTests.OperacaoFactory>
{
    private readonly OperacaoFactory _factory;
    public OperacaoRenderTests(OperacaoFactory factory) => _factory = factory;

    [Fact]
    public async Task Pagina_Operacao_renderiza_em_linguagem_clara()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/Operacao");
        var html = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "render falhou. body: {0}",
            html.Length > 1500 ? html[..1500] : html);

        html.Should().Contain("operacaoClientes");
        html.Should().Contain("Como estão os seus clientes");

        // sem o jargão da versão anterior
        html.Should().NotContain("Centro de Comando");
        html.Should().NotContain("Radar de Risco");

        // nome do cliente vai por x-text (Alpine auto-escapa) — XSS-safe; nada de x-html nem double-encoding
        html.Should().Contain("x-text=\"c.nome\"");
        html.Should().NotContain("x-html");
        html.Should().NotContain("&amp;lt;");
        html.Should().NotContain("&amp;gt;");
    }

    [Fact]
    public async Task Proxy_operacao_fleet_repassa_o_json_da_api()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/api-proxy/admin/operacao/fleet");
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Loja Stub");
        body.Should().Contain("clientes");
    }

    public sealed class OperacaoFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();
                services.AddHttpClient<AdminApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new OperacaoStubHandler());
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

    private sealed class OperacaoStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var payload = new
            {
                data = new
                {
                    generated = "2026-06-16T12:00:00Z",
                    totalClientes = 1,
                    totais = new
                    {
                        clientesAtivos = 1,
                        precisamAtencao = 0,
                        vendasHojeTotal = 500m,
                        mrrAtivo = 200m,
                        ticketsSlaViolado = 0,
                        faturasVencidasValor = 0m,
                        suspensos = 0,
                    },
                    clientes = new object[]
                    {
                        new
                        {
                            empresaId = Guid.NewGuid(),
                            nome = "Loja Stub",
                            plano = "Plus",
                            mrr = 200m,
                            statusAssinatura = "Ativa",
                            statusBand = "ok",
                            motivos = Array.Empty<string>(),
                            vendasHoje = 500m,
                            vendasHojeCount = 2,
                            ticketsAbertos = 0,
                            ticketsSlaViolado = 0,
                            faturasVencidasCount = 0,
                            faturasVencidasValor = 0m,
                            ultimaVendaEm = (string?)null,
                            trialFim = (string?)null,
                            severidade = 0,
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(payload);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
