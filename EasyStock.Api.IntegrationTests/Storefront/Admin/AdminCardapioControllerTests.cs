using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Admin;

/// <summary>
/// Integration tests para a gestão admin/tenant do cardápio (ADR-0031). Sobe Postgres real
/// via Testcontainers — necessário porque os comportamentos sob teste (índice único parcial
/// de avulso → 23505 → 400; escopo de empresa → 404) dependem de constraints do PG que
/// mock/in-memory não reproduz (exatamente a classe de bug do #567).
/// </summary>
public sealed class AdminCardapioControllerTests : IAsyncLifetime
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
                .WithDatabase("easystock_admin_cardapio_tests")
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
                    });
                });
            });
    }

    // empresaId null = não inclui o claim (simula Admin com 2+ empresas sem seleção, ou SuperAdmin).
    private static string GerarJwt(string nivel, Guid? empresaId = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("nivel", nivel),
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresaId", empresaId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<StorefrontEntity> SeedStorefrontAsync(
        WebApplicationFactory<Program> factory, Guid empresaId, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "E2E Cardapio",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });

        var sf = StorefrontEntity.Criar(empresaId, slug, "Loja E2E", 0m);
        sf.Ativar();
        db.Storefronts.Add(sf);

        await db.SaveChangesAsync();
        return sf;
    }

    private static async Task<(StorefrontEntity Storefront, Guid ItemId)> SeedStorefrontComItemAsync(
        WebApplicationFactory<Program> factory, Guid empresaId, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "E2E Cardapio Item",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });

        var sf = StorefrontEntity.Criar(empresaId, slug, "Loja E2E Item", 0m);
        sf.Ativar();
        db.Storefronts.Add(sf);

        var item = CardapioItem.CriarAvulso(sf.Id, "Pão de Alho", 18.00m, "Acompanhamentos");
        item.TornarVisivel();
        db.CardapioItens.Add(item);

        await db.SaveChangesAsync();
        return (sf, item.Id);
    }

    // ── Nome avulso duplicado → 23505 → 400 (via tenant controller) ────────

    [SkippableFact]
    public async Task POST_ItemAvulsoNomeDuplicado_Returns400_ComMensagem()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaId = Guid.NewGuid();
        await SeedStorefrontAsync(factory, empresaId, "loja-dup");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresaId));

        var body = new
        {
            produtoId = (Guid?)null,
            nomePublico = "Pão de Alho",
            precoStorefront = 18.0m,
            ordemExibicao = 1.0,
            visivel = false,
        };

        var primeiro = await client.PostAsJsonAsync("/api/minha-vitrine/cardapio", body);
        primeiro.StatusCode.Should().Be(HttpStatusCode.Created, "primeiro avulso entra normal");

        var duplicado = await client.PostAsJsonAsync("/api/minha-vitrine/cardapio", body);
        duplicado.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "índice único parcial uq_cardapio_item_storefront_nome_avulso → 23505 → 400");

        var json = await duplicado.Content.ReadAsStringAsync();
        json.Should().Contain("Já existe um item com esse nome no cardápio.");
    }

    // ── Escopo de empresa: Admin de outra empresa → 404 (IDOR fix) ─────────

    [SkippableFact]
    public async Task Admin_PostNoStorefrontDeOutraEmpresa_Returns404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaDona = Guid.NewGuid();
        var sf = await SeedStorefrontAsync(factory, empresaDona, "loja-dona");

        using var client = factory.CreateClient();
        // JWT de Admin de OUTRA empresa tentando operar o storefront alheio por GUID.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", Guid.NewGuid()));

        var body = new
        {
            produtoId = (Guid?)null,
            nomePublico = "Invasor",
            precoStorefront = 9.0m,
            ordemExibicao = 1.0,
            visivel = false,
        };

        var resp = await client.PostAsJsonAsync($"/api/admin/storefronts/{sf.Id}/cardapio", body);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Admin de outra empresa não opera storefront alheio — 404 (não 403), não vaza existência");
    }

    // ── p2 (auditoria): Admin sem empresaId no token → 400 claro ───────────

    [SkippableFact]
    public async Task Admin_SemEmpresaIdNoToken_Returns400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var sf = await SeedStorefrontAsync(factory, Guid.NewGuid(), "loja-sem-emp");

        using var client = factory.CreateClient();
        // Admin SEM claim empresaId (ex.: usuário com 2+ empresas, sem seleção) → guard p2.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin"));

        var resp = await client.GetAsync($"/api/admin/storefronts/{sf.Id}/cardapio");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Admin sem empresa vinculada recebe 400 claro, não 404 silencioso (auditoria p2)");
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("não está vinculada a uma empresa");
    }

    // ── Escopo item-level contra PG real: editar item de outra empresa → 404 ──

    [SkippableFact]
    public async Task Admin_EditarItemDeOutraEmpresa_Returns404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaDona = Guid.NewGuid();
        var (sf, itemId) = await SeedStorefrontComItemAsync(factory, empresaDona, "loja-item-dona");

        using var client = factory.CreateClient();
        // Admin de OUTRA empresa tenta editar um item EXISTENTE do storefront alheio.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", Guid.NewGuid()));

        var body = new { nomePublico = "hackeado", precoStorefront = 1.0m };
        var resp = await client.PutAsJsonAsync($"/api/admin/storefronts/{sf.Id}/cardapio/{itemId}", body);

        // GetByIdAndScopeAsync filtra por empresa no PG real → item não encontrado p/ o tenant A → 404.
        // (CardapioItem não tem global query filter; este é o caminho do IDOR item-level, agora fechado.)
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "escopo item-level bloqueia edição de item de outra empresa contra Postgres real");
    }
}
