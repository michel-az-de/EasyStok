using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Avaliacao;

/// <summary>
/// Testes E2E do <see cref="AvaliacaoController"/> (TASK-EZ-AVAL-001).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>JWT inválido em GET /avaliar/abrir → 410 Gone.</item>
///   <item>POST sem cookie → 401 Unauthorized.</item>
///   <item>Ciclo completo: abrir → cookie → POST 201 → GET listagem 200.</item>
/// </list>
/// </summary>
public sealed class AvaliacaoControllerTests
{
    private static readonly Guid PedidoId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";
    private const string JwtSecret = "test-avaliacao-jwt-secret-min32chars!!";

    // ── Factory ────────────────────────────────────────────────────────────

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
                        ["Jwt:Key"] = "test-super-secret-key-min32chars!!",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                        ["Avaliacao:JwtSecret"] = JwtSecret,
                    });
                });

                b.ConfigureServices(services =>
                {
                    // Mock repositórios — evita DB real nos testes de integração de controller
                    var pedidoRepoDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IPedidoStorefrontRepository));
                    if (pedidoRepoDesc is not null) services.Remove(pedidoRepoDesc);
                    services.AddScoped(_ => BuildPedidoRepoMock());

                    var avaliacaoRepoDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IPedidoAvaliacaoRepository));
                    if (avaliacaoRepoDesc is not null) services.Remove(avaliacaoRepoDesc);
                    services.AddScoped(_ => BuildAvaliacaoRepoMock());

                    var storefrontRepoDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IStorefrontRepository));
                    if (storefrontRepoDesc is not null) services.Remove(storefrontRepoDesc);
                    services.AddScoped(_ => BuildStorefrontRepoMock());

                    extraServices?.Invoke(services);
                });
            });
    }

    private static IPedidoStorefrontRepository BuildPedidoRepoMock()
    {
        var mock = Substitute.For<IPedidoStorefrontRepository>();
        var pedido = new Pedido
        {
            Id = PedidoId,
            EmpresaId = EmpresaId,
            ClienteId = ClienteId,
            ClienteNome = "Maria",
            Status = Domain.Sales.StatusPedidoMapper.Entregue,
            EntreguEm = DateTime.UtcNow.AddHours(-25),
            AvaliacaoSolicitadaEm = DateTime.UtcNow.AddHours(-24),
            CriadoEm = DateTime.UtcNow.AddDays(-2),
            AlteradoEm = DateTime.UtcNow.AddDays(-2),
        };
        mock.GetByIdAsync(PedidoId, Arg.Any<CancellationToken>()).Returns(pedido);
        return mock;
    }

    private static IPedidoAvaliacaoRepository BuildAvaliacaoRepoMock()
    {
        var mock = Substitute.For<IPedidoAvaliacaoRepository>();
        mock.GetByPedidoAsync(PedidoId, Arg.Any<CancellationToken>()).Returns((PedidoAvaliacao?)null);

        var avaliacao = PedidoAvaliacao.Criar(
            PedidoId, ClienteId, EmpresaId, 5, "Ótimo serviço!", true, null,
            DateTime.UtcNow.AddHours(-24));
        mock.GetVisiveisDaEmpresaAsync(EmpresaId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PedidoAvaliacao> { avaliacao });
        return mock;
    }

    private static IStorefrontRepository BuildStorefrontRepoMock()
    {
        var mock = Substitute.For<IStorefrontRepository>();
        var sf = StorefrontEntity.Criar(EmpresaId, Slug, "Casa da Babá", 0m);
        sf.Ativar();
        mock.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(sf);
        return mock;
    }

    private static string GerarJwtValido(Guid pedidoId)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Avaliacao:JwtSecret"] = JwtSecret })
            .Build();
        return new AvaliacaoTokenService(cfg, TimeProvider.System).Gerar(pedidoId);
    }

    // ── Testes ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AbrirAvaliacao_TokenInvalido_Retorna410()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes de integração de controller.");

        using var factory = CriarFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/avaliar/abrir?p={PedidoId}&t=token.invalido.xxx");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [SkippableFact]
    public async Task CriarAvaliacao_SemCookie_Retorna401()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes de integração de controller.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();

        var body = new { pedidoId = PedidoId, nota = 5, comentario = "Ótimo!", recomendariaParaAmigos = true };
        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/avaliacoes", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task E2E_AbrirCookiePostListar_CicloConcluido()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes de integração de controller.");

        using var factory = CriarFactory();

        // ── 1. GET /avaliar/abrir → 302 + Set-Cookie ─────────────────────
        var clientSemRedirect = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var token = GerarJwtValido(PedidoId);
        var abrirResp = await clientSemRedirect.GetAsync($"/avaliar/abrir?p={PedidoId}&t={token}");

        abrirResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        abrirResp.Headers.Location?.ToString().Should().Be($"/avaliar/{PedidoId}");

        // Extrai cookie do header Set-Cookie
        abrirResp.Headers.TryGetValues("Set-Cookie", out var setCookieValues).Should().BeTrue();
        var setCookieHeader = setCookieValues!.First();
        var cookiePart = setCookieHeader.Split(';').First();
        var cookieName = cookiePart.Split('=').First().Trim();
        var cookieValue = cookiePart.Split('=', 2).Last().Trim();

        cookieName.Should().Be($"__Host-cdb_aval_{PedidoId}");
        cookieValue.Should().NotBeNullOrEmpty();

        // ── 2. POST /api/storefront/{slug}/avaliacoes com cookie → 201 ────
        var clientComCookie = factory.CreateClient();
        clientComCookie.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={cookieValue}");

        var body = new
        {
            pedidoId = PedidoId,
            nota = 5,
            comentario = "Serviço excelente!",
            recomendariaParaAmigos = true,
        };
        var postResp = await clientComCookie.PostAsJsonAsync($"/api/storefront/{Slug}/avaliacoes", body);

        postResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var postJson = await postResp.Content.ReadFromJsonAsync<JsonDocument>();
        postJson!.RootElement.TryGetProperty("id", out _).Should().BeTrue();
        postJson.RootElement.TryGetProperty("nota", out var notaEl).Should().BeTrue();
        notaEl.GetInt32().Should().Be(5);

        // ── 3. GET /api/storefront/{slug}/avaliacoes → 200 com itens ──────
        var getResp = await clientComCookie.GetAsync($"/api/storefront/{Slug}/avaliacoes");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listJson = await getResp.Content.ReadFromJsonAsync<JsonDocument>();
        listJson!.RootElement.TryGetProperty("total", out var totalEl).Should().BeTrue();
        totalEl.GetInt32().Should().BeGreaterThan(0);
    }
}
