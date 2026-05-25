using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Agendamento;

/// <summary>
/// Integration tests para GET /api/storefront/{slug}/janelas (TASK-EZ-AGEND-001).
///
/// <para>
/// Sobe Postgres real via Testcontainers, roda migrations, semeia Storefront +
/// FreteZona + JanelaEntrega + VagaOcupada, faz request HTTP, valida response
/// + Cache-Control. Sem Docker os testes viram no-op via <c>_isAvailable</c>.
/// </para>
/// </summary>
public sealed class AgendamentoControllerTests : IAsyncLifetime
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
                .WithDatabase("easystock_agendamento_tests")
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
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL não disponível.");

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
                    });
                });
            });
    }

    // ── Seed helpers ─────────────────────────────────────────────────────

    private sealed record SeedResult(
        StorefrontEntity Storefront,
        FreteZona Zona,
        JanelaEntrega Janela);

    private static async Task<SeedResult> SeedBaseAsync(
        WebApplicationFactory<Program> factory,
        string slug,
        DateOnly dataEntrega)
    {
        using var scope = factory.Services.CreateScope();
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var freteRepo = scope.ServiceProvider.GetRequiredService<IFreteZonaRepository>();
        var janelaRepo = scope.ServiceProvider.GetRequiredService<IJanelaEntregaRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var storefront = StorefrontEntity.Criar(
            empresaId: Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa da Babá (it-agend)",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();
        await storefrontRepo.AddAsync(storefront);

        var zona = FreteZona.CriarPorCep(
            storefrontId: storefront.Id,
            label: "Zona Teste",
            cepInicio: "01000000",
            cepFim: "09999999",
            valor: 10m,
            tempoEstimadoMinutos: 30);
        await freteRepo.AddAsync(zona);

        var diaDaSemana = (int)dataEntrega.DayOfWeek;
        var janela = JanelaEntrega.Criar(
            storefrontId: storefront.Id,
            diaDaSemana: diaDaSemana,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 2,
            label: "Manhã 9-12h");
        await janelaRepo.AddAsync(janela);

        await uow.CommitAsync();
        return new SeedResult(storefront, zona, janela);
    }

    private static async Task SeedVagaOcupadaAsync(
        WebApplicationFactory<Program> factory,
        Guid janelaId,
        DateOnly dataEntrega)
    {
        using var scope = factory.Services.CreateScope();
        var vagaRepo = scope.ServiceProvider.GetRequiredService<IVagaOcupadaRepository>();
        await vagaRepo.OcuparAsync(janelaId, dataEntrega, Guid.NewGuid());
    }

    // ── Testes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJanelas_HappyPath_Retorna200ComVagasECacheControl()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // Usar data fixa futura (segunda-feira)
        var data = new DateOnly(2026, 6, 8); // segunda-feira
        var seed = await SeedBaseAsync(factory, "casa-da-baba-agit1", data);

        // 1 vaga ocupada de 2
        await SeedVagaOcupadaAsync(factory, seed.Janela.Id, data);

        var resp = await client.GetAsync(
            $"/api/storefront/{seed.Storefront.Slug}/janelas?dataInicio={data:yyyy-MM-dd}&dataFim={data:yyyy-MM-dd}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<List<JanelaDisponivelResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.Should().NotBeNull();
        body!.Should().HaveCount(1);
        body[0].JanelaId.Should().Be(seed.Janela.Id);
        body[0].VagasRestantes.Should().Be(1, "2 capacidade - 1 ocupada = 1 restante");
        body[0].Capacidade.Should().Be(2);
        body[0].Esgotado.Should().BeFalse();
        body[0].Label.Should().Be("Manhã 9-12h");

        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.Public.Should().BeTrue();
        resp.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task GetJanelas_CepForaDaZona_Retorna422()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var data = new DateOnly(2026, 6, 8);
        var seed = await SeedBaseAsync(factory, "casa-da-baba-agit2", data);

        // CEP 99000000 fora da zona 01000000-09999999
        var resp = await client.GetAsync(
            $"/api/storefront/{seed.Storefront.Slug}/janelas?dataInicio={data:yyyy-MM-dd}&dataFim={data:yyyy-MM-dd}&cep=99000000");

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task GetJanelas_StorefrontInexistente_Retorna404()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var data = new DateOnly(2026, 6, 8);
        var resp = await client.GetAsync(
            $"/api/storefront/slug-inexistente-agit3/janelas?dataInicio={data:yyyy-MM-dd}&dataFim={data:yyyy-MM-dd}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DTO espelho da response ─────────────────────────────────────────

    private sealed record JanelaDisponivelResponse(
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("janelaId")] Guid JanelaId,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("horaInicio")] string HoraInicio,
        [property: JsonPropertyName("horaFim")] string HoraFim,
        [property: JsonPropertyName("vagasRestantes")] int VagasRestantes,
        [property: JsonPropertyName("capacidade")] int Capacidade,
        [property: JsonPropertyName("esgotado")] bool Esgotado);
}
