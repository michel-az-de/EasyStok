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
/// Poka-yoke da rota de detalhe de fatura por NUMERO legivel (ex.: 2026-000001). A rota
/// (antes {id:guid}) dava 404 para o numero; agora aceita guid OU numero e canonicaliza
/// para a URL por guid, resolvendo via a busca da listagem admin (a query `busca` casa em
/// Numero no repositorio). VERMELHO se a rota voltar a so aceitar guid ou o resolver quebrar.
/// </summary>
public class FaturasDetailRouteTests : IClassFixture<FaturasDetailRouteTests.AdminFactory>
{
    public static readonly Guid FaturaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public const string Numero = "2026-000001";

    private readonly AdminFactory _factory;
    public FaturasDetailRouteTests(AdminFactory factory) => _factory = factory;

    [Fact]
    public async Task Detail_por_numero_legivel_redireciona_para_a_url_por_guid()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync($"/Faturas/Detail/{Numero}");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect, "o numero legivel deve resolver, nao dar 404");
        resp.Headers.Location!.ToString().Should().Contain($"/Faturas/Detail/{FaturaId}",
            "deve canonicalizar para a URL por guid");
    }

    public sealed class AdminFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AdminSessionService, FakeSuperAdminSession>();
                services.AddHttpClient<AdminApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new FaturaStubHandler());
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

    private sealed class FaturaStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            object payload = url.Contains("busca=")
                // Listagem admin: `data` e um array de FaturaResumoDto (id + numero).
                ? new
                {
                    data = new object[]
                    {
                        new { id = FaturaId, numero = Numero, status = "Emitida", origem = "Avulsa", total = 1000.00, moeda = "BRL" }
                    },
                    meta = new { total = 1, pages = 1 }
                }
                // Detalhe por guid (se o teste seguir o redirect): envelope minimo.
                : new
                {
                    data = new
                    {
                        id = FaturaId, numero = Numero, status = "Emitida", origem = "Avulsa",
                        moeda = "BRL", total = 1000.00,
                        itens = Array.Empty<object>(), pagamentos = Array.Empty<object>(), eventos = Array.Empty<object>()
                    }
                };

            var json = JsonSerializer.Serialize(payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
