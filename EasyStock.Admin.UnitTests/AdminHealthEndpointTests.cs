using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// Poka-yoke do <c>/health</c> do Admin (triagem QA Admin v1.10). Garante que o endpoint
/// (1) existe, (2) é ANÔNIMO — responde 200 sem sessão, não 302/login — e (3) carimba
/// status + version + o commit do <c>GIT_SHA</c> do build. Sem isto o Admin não tinha como
/// provar qual commit está no ar e toda triagem de QA ficava cega a "stale" (espelha o
/// /health do Web, #453). VERMELHO se alguém remover o endpoint, colocá-lo atrás de auth,
/// ou trocar o carimbo do GIT_SHA por um literal.
/// </summary>
public class AdminHealthEndpointTests : IClassFixture<AdminHealthEndpointTests.HealthFactory>
{
    private readonly HealthFactory _factory;
    public AdminHealthEndpointTests(HealthFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_anonimo_expoe_status_version_e_commit_do_GIT_SHA()
    {
        // Sem cookie de sessão de propósito: o stale-check roda sem login.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "o /health deve ser anônimo (200), não 302/login");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("healthy");
        root.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        // commit reflete o GIT_SHA injetado pelo build (setado pela factory antes do host subir).
        root.GetProperty("commit").GetString().Should().Be(HealthFactory.KnownSha);
    }

    public sealed class HealthFactory : WebApplicationFactory<Program>
    {
        public const string KnownSha = "test-sha-deadbeef";

        // Setado no ctor: roda ANTES do host construir (em CreateClient), então o top-level do
        // Program lê este valor em Environment.GetEnvironmentVariable("GIT_SHA").
        public HealthFactory() => Environment.SetEnvironmentVariable("GIT_SHA", KnownSha);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Startup do Admin exige ApiBaseUrl (fail-fast). Valor de teste (o /health não chama a API).
            builder.UseSetting("ApiBaseUrl", "https://api.test.local");
            builder.UseEnvironment("Development");
        }
    }
}
