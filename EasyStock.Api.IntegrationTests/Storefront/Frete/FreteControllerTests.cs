using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Frete;

/// <summary>
/// Integration tests para GET /api/storefront/{slug}/frete?cep=... (TASK-EZ-FRETE-001).
///
/// <para>
/// Sobe Postgres real via Testcontainers, roda migrations, semeia Storefront +
/// FreteZona, faz request HTTP, valida response + cache-control. Em ambientes
/// sem Docker, tests viram no-op via <c>_isAvailable</c> (padrao do projeto).
/// </para>
/// </summary>
public sealed class FreteControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_storefront_frete_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL nao disponivel.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _pg!.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = JwtIssuer,
                        ["Jwt:Audience"] = JwtAudience,
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local",
                        // Garante NoOp em testes — sem internet
                        ["Storefront:Frete:EnableViaCepLookup"] = "false",
                    });
                });
            });
    }

    private static async Task<StorefrontEntity> SeedStorefrontComZonaCepAsync(
        WebApplicationFactory<Program> factory,
        string slug,
        string cepInicio,
        string cepFim,
        decimal valor,
        int tempoMin,
        string label = "Centro")
    {
        using var scope = factory.Services.CreateScope();
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var freteRepo = scope.ServiceProvider.GetRequiredService<IFreteZonaRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var storefront = StorefrontEntity.Criar(
            empresaId: Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa da Baba (it-frete)",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();
        await storefrontRepo.AddAsync(storefront);

        var zona = FreteZona.CriarPorCep(
            storefrontId: storefront.Id,
            label: label,
            cepInicio: cepInicio,
            cepFim: cepFim,
            valor: valor,
            tempoEstimadoMinutos: tempoMin);
        await freteRepo.AddAsync(zona);
        await uow.CommitAsync();

        return storefront;
    }

    // ── Happy path ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetFrete_CepEmZonaAtiva_Retorna200ComValorECacheable()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontComZonaCepAsync(
            factory, slug: "casa-da-baba-itf1",
            cepInicio: "05000000", cepFim: "05999999",
            valor: 15m, tempoMin: 30, label: "Centro");

        var resp = await client.GetAsync(
            $"/api/storefront/{storefront.Slug}/frete?cep=05500-000");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<FreteCalculadoResponse>();
        body!.Valor.Should().Be(1500);
        body.ValorFormatado.Should().Be("R$ 15,00");
        body.EtaLabel.Should().Be("30 min");
        body.ZonaLabel.Should().Be("Centro");

        // Cache-Control: public, max-age=86400
        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.Public.Should().BeTrue();
        resp.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromHours(24));
    }

    [SkippableFact]
    public async Task GetFrete_CepSemMascara_TambemAceito()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontComZonaCepAsync(
            factory, slug: "casa-da-baba-itf2",
            cepInicio: "05000000", cepFim: "05999999",
            valor: 10m, tempoMin: 45);

        var resp = await client.GetAsync(
            $"/api/storefront/{storefront.Slug}/frete?cep=05500000");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Sem cobertura → 422 ────────────────────────────────────────────

    [SkippableFact]
    public async Task GetFrete_CepForaDaZona_Retorna422ENaoCacheia()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontComZonaCepAsync(
            factory, slug: "casa-da-baba-itf3",
            cepInicio: "05000000", cepFim: "05999999",
            valor: 15m, tempoMin: 30);

        var resp = await client.GetAsync(
            $"/api/storefront/{storefront.Slug}/frete?cep=09000000"); // fora

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    // ── CEP invalido → 400 ─────────────────────────────────────────────

    [SkippableFact]
    public async Task GetFrete_CepInvalido_Retorna400ENaoCacheia()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontComZonaCepAsync(
            factory, slug: "casa-da-baba-itf4",
            cepInicio: "05000000", cepFim: "05999999",
            valor: 15m, tempoMin: 30);

        var resp = await client.GetAsync(
            $"/api/storefront/{storefront.Slug}/frete?cep=abc");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [SkippableFact]
    public async Task GetFrete_CepVazio_Retorna400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontComZonaCepAsync(
            factory, slug: "casa-da-baba-itf5",
            cepInicio: "05000000", cepFim: "05999999",
            valor: 15m, tempoMin: 30);

        var resp = await client.GetAsync($"/api/storefront/{storefront.Slug}/frete?cep=");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Storefront inexistente → 404 ───────────────────────────────────

    [SkippableFact]
    public async Task GetFrete_StorefrontInexistente_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync(
            "/api/storefront/slug-que-nao-existe/frete?cep=05500-000");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DTO espelho da response ────────────────────────────────────────

    private sealed record FreteCalculadoResponse(
        Guid ZonaId,
        int Valor,
        string ValorFormatado,
        string EtaLabel,
        string ZonaLabel);
}
