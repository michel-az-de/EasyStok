using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Menu;

/// <summary>
/// Integration tests para GET /api/storefront/{slug}/menu (TASK-EZ-MENU-001).
///
/// <para>
/// Sobe Postgres real via Testcontainers, roda migrations, semeia
/// Empresa+Categoria+Produto+Storefront+CardapioItem e bate HTTP. Em ambientes
/// sem Docker (CI sem privilégio, dev local sem Docker Desktop), tests viram
/// no-op via guarda <c>_isAvailable</c>.
/// </para>
/// </summary>
public sealed class MenuControllerTests : IAsyncLifetime
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
                .WithDatabase("easystock_storefront_menu_tests")
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

    private sealed record SeedResult(
        Guid EmpresaId,
        StorefrontEntity Storefront,
        CardapioItem ItemVisivel,
        CardapioItem ItemOculto);

    /// <summary>
    /// Seeda Empresa + Categoria + 2 Produtos + Storefront ativo + 2 CardapioItens
    /// (um visível, um oculto). Retorna IDs/instâncias pra asserções.
    /// </summary>
    private static async Task<SeedResult> SeedCardapioAsync(
        WebApplicationFactory<Program> factory,
        string slug,
        bool storefrontAtivo = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Casa da Babá (test)",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Empresas.Add(empresa);

        var categoria = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Pratos principais",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Categorias.Add(categoria);

        var produtoVisivel = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = categoria.Id,
            Nome = "Lasanha de berinjela",
            Tipo = TipoProduto.Alimento,
            PrecoReferencia = Dinheiro.FromDecimal(42.50m),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        var produtoOculto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = categoria.Id,
            Nome = "Em rascunho",
            Tipo = TipoProduto.Alimento,
            PrecoReferencia = Dinheiro.FromDecimal(10m),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Produtos.Add(produtoVisivel);
        db.Produtos.Add(produtoOculto);

        var storefront = StorefrontEntity.Criar(
            empresaId: empresa.Id,
            slug: slug,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo) storefront.Ativar();
        db.Storefronts.Add(storefront);

        var itemVisivel = CardapioItem.CriarAPartirDeProduto(storefront.Id, produtoVisivel);
        itemVisivel.AtualizarMetadata(
            descricaoPublica: "Massa fresca com molho da casa",
            fotoUrl: "https://cdn/lasanha.jpg");
        itemVisivel.DefinirOrdem(1.0);
        itemVisivel.TornarVisivel();

        var itemOculto = CardapioItem.CriarAPartirDeProduto(storefront.Id, produtoOculto);
        // intencionalmente NÃO chama TornarVisivel — fica oculto

        db.CardapioItens.Add(itemVisivel);
        db.CardapioItens.Add(itemOculto);

        await db.SaveChangesAsync();
        return new SeedResult(empresa.Id, storefront, itemVisivel, itemOculto);
    }

    // ── Happy path ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetMenu_StorefrontComItens_Retorna200ComCacheControlEEtag()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-menu-1");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.CacheControl!.Public.Should().BeTrue();
        resp.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        resp.Headers.CacheControl.SharedMaxAge.Should().Be(TimeSpan.FromMinutes(5));
        resp.Headers.ETag.Should().NotBeNull("ETag obrigatório para suportar If-None-Match");
        resp.Headers.ETag!.Tag.Should().StartWith("\"").And.EndWith("\"");

        var json = await resp.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<MenuEnvelope>(json, JsonOpts)!;
        envelope.TituloPublico.Should().Be("Casa da Babá", "o envelope carrega o título público do storefront");
        envelope.Slug.Should().Be(seed.Storefront.Slug);
        var itens = envelope.Itens;
        itens.Should().HaveCount(1, "apenas o item Visivel=true deve aparecer");
        var dto = itens[0];
        dto.Id.Should().Be(seed.ItemVisivel.Id);
        dto.Nome.Should().Be("Lasanha de berinjela");
        dto.PrecoCentavos.Should().Be(4250);
        dto.Categoria.Should().Be("Pratos principais");
        dto.ImagemUrl.Should().Be("https://cdn/lasanha.jpg");
    }

    // ── Contrato envelope (guard de forma — #643) ──────────────────────

    [SkippableFact]
    public async Task GetMenu_RespostaEhEnvelopeObjeto_NaoArrayNu()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-envelope");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "contrato canônico é envelope { itens, ... }, NÃO array nu (menu.js exige Array.isArray(data.itens))");
        doc.RootElement.TryGetProperty("itens", out var itens).Should().BeTrue("envelope tem 'itens'");
        itens.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.TryGetProperty("tituloPublico", out var titulo).Should().BeTrue("envelope tem 'tituloPublico'");
        titulo.GetString().Should().Be("Casa da Babá");
        doc.RootElement.TryGetProperty("slug", out _).Should().BeTrue("envelope tem 'slug'");
    }

    [SkippableFact]
    public async Task GetMenu_ItemTemExatamenteAs10Chaves_InclusiveNulls()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        // Avulso (Pão de Alho): descricao/imagemUrl/tag null, estoqueAtual null — prova que nulls
        // são EMITIDOS (PublicJsonOptions.DefaultIgnoreCondition = Never), não omitidos.
        var seed = await SeedComAvulsoAsync(factory, slug: "casa-da-baba-paridade");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var avulso = doc.RootElement.GetProperty("itens").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == seed.Avulso.Id);

        var chaves = avulso.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        chaves.Should().BeEquivalentTo(new[]
        {
            "categoria", "descricao", "disponivel", "estoqueAtual", "id",
            "imagemUrl", "nome", "ordem", "precoCentavos", "tag",
        }, "exatamente as 10 chaves camelCase do contrato, inclusive as null");

        avulso.GetProperty("descricao").ValueKind.Should().Be(JsonValueKind.Null);
        avulso.GetProperty("imagemUrl").ValueKind.Should().Be(JsonValueKind.Null);
        avulso.GetProperty("estoqueAtual").ValueKind.Should().Be(JsonValueKind.Null, "avulso não tem snapshot de estoque");
        avulso.GetProperty("tag").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── ETag / 304 ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetMenu_IfNoneMatchBate_Retorna304()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-menu-etag");

        // 1ª request: pega o ETag
        var resp1 = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = resp1.Headers.ETag!.Tag;

        // 2ª request com If-None-Match igual ao ETag → 304
        var req2 = new HttpRequestMessage(HttpMethod.Get, $"/api/storefront/{seed.Storefront.Slug}/menu");
        req2.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var resp2 = await client.SendAsync(req2);

        resp2.StatusCode.Should().Be(HttpStatusCode.NotModified);
        resp2.Headers.ETag!.Tag.Should().Be(etag);
    }

    [SkippableFact]
    public async Task GetMenu_IfNoneMatchDiferente_RetornaPayloadComEtagNovo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-menu-etag-miss");

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/storefront/{seed.Storefront.Slug}/menu");
        req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"hash-velho-que-nao-bate\""));
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 404 ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetMenu_SlugInexistente_Retorna404ComProblemDetails()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/storefront/slug-que-nao-existe/menu");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Content.Headers.ContentType?.MediaType.Should()
            .BeOneOf("application/problem+json", "application/json");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Storefront não encontrado");
    }

    [SkippableFact]
    public async Task GetMenu_StorefrontInativo_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-inativo", storefrontAtivo: false);

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "storefront inativo é equivalente a inexistente do ponto de vista do cliente");
    }

    // ── Sem auth ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetMenu_SemHeaderAuthorization_Retorna200()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        // intencionalmente NÃO seta Authorization header

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-anon");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "endpoint é AllowAnonymous — não exige cookie nem JWT");
    }

    // ── Impressão (ADR-0031 fatia 9) ───────────────────────────────────

    [SkippableFact]
    public async Task GetImprimir_StorefrontComItens_RetornaHtmlComTituloENoStore()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedCardapioAsync(factory, slug: "casa-da-baba-print");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu/imprimir");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        resp.Headers.CacheControl?.NoStore.Should().BeTrue(
            "impressão carimba data/hora local — não pode ser cacheada");

        var html = await resp.Content.ReadAsStringAsync();
        html.Should().Contain("Casa da Babá", "cabeçalho usa o título público do storefront");
        html.Should().Contain("Lasanha de berinjela", "item visível deve aparecer");
        html.Should().NotContain("Em rascunho", "item oculto (Visivel=false) não vai para impressão");
        html.Should().Contain("window.print()", "botão de imprimir presente (escondido no @media print)");
    }

    [SkippableFact]
    public async Task GetImprimir_SlugInexistente_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/storefront/nao-existe-print/menu/imprimir");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Item avulso (ADR-0031) ─────────────────────────────────────────

    private sealed record SeedAvulsoResult(StorefrontEntity Storefront, CardapioItem Vinculado, CardapioItem Avulso);

    /// <summary>
    /// Seeda Storefront ativo + 1 item vinculado (visível) + 1 item avulso (visível, ADR-0031).
    /// O avulso não tem Produto: nome/preço/categoria vêm do próprio CardapioItem.
    /// </summary>
    private static async Task<SeedAvulsoResult> SeedComAvulsoAsync(
        WebApplicationFactory<Program> factory, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Casa da Babá (avulso test)",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Empresas.Add(empresa);

        var categoria = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Massas",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Categorias.Add(categoria);

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = categoria.Id,
            Nome = "Lasanha",
            Tipo = TipoProduto.Alimento,
            PrecoReferencia = Dinheiro.FromDecimal(42.50m),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        db.Produtos.Add(produto);

        var storefront = StorefrontEntity.Criar(empresa.Id, slug, "Casa da Babá", 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);

        var vinculado = CardapioItem.CriarAPartirDeProduto(storefront.Id, produto);
        vinculado.DefinirOrdem(1.0);
        vinculado.TornarVisivel();

        // Avulso (ADR-0031): sem ProdutoId; nome/preço/categoria no próprio item.
        var avulso = CardapioItem.CriarAvulso(storefront.Id, "Pão de Alho", 18.00m, "Acompanhamentos");
        avulso.DefinirOrdem(2.0);
        avulso.TornarVisivel();

        db.CardapioItens.Add(vinculado);
        db.CardapioItens.Add(avulso);

        await db.SaveChangesAsync();
        return new SeedAvulsoResult(storefront, vinculado, avulso);
    }

    [SkippableFact]
    public async Task GetMenu_RetornaItemAvulso_ComNomePublico()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedComAvulsoAsync(factory, slug: "casa-da-baba-avulso");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var itens = JsonSerializer.Deserialize<MenuEnvelope>(
            await resp.Content.ReadAsStringAsync(), JsonOpts)!.Itens;
        var avulso = itens.Single(i => i.Id == seed.Avulso.Id);
        avulso.Nome.Should().Be("Pão de Alho", "NomePublico avulso (minúsculo no banco) é title-cased pt-BR na projeção pública");
        avulso.PrecoCentavos.Should().Be(1800);
        avulso.EstoqueAtual.Should().BeNull("avulso (ProdutoId null) não tem snapshot de estoque do ERP");
        avulso.Categoria.Should().Be("Acompanhamentos", "CategoriaTexto avulsa também é title-cased pra exibição");
        avulso.Disponivel.Should().BeTrue("front usa Disponivel, não EstoqueAtual, para 'Esgotado'");
    }

    [SkippableFact]
    public async Task GetMenu_MixAvulsoVinculado_NaoLancaException_RetornaAmbos()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var seed = await SeedComAvulsoAsync(factory, slug: "casa-da-baba-mix");

        var resp = await client.GetAsync($"/api/storefront/{seed.Storefront.Slug}/menu");

        // Valida que GetVisiveisDoStorefrontAsync (OrderBy c.Produto!.Categoria!.Nome SQL-side)
        // traduz para LEFT JOIN com NULLs (item avulso) sem NRE contra Postgres real — exatamente
        // a classe de risco do #567 que o mock LINQ-to-Objects não pega (ADR-0031 §3).
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var itens = JsonSerializer.Deserialize<MenuEnvelope>(
            await resp.Content.ReadAsStringAsync(), JsonOpts)!.Itens;
        itens.Should().HaveCount(2);
        itens.Select(i => i.Id).Should().Contain(new[] { seed.Vinculado.Id, seed.Avulso.Id });
    }

    // ── DTO espelho ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record MenuItemDto(
        Guid Id,
        string Nome,
        string? Descricao,
        long PrecoCentavos,
        string? ImagemUrl,
        int? EstoqueAtual,   // null para itens avulsos (ADR-0031)
        string? Categoria,
        double Ordem,
        bool Disponivel,
        string? Tag);

    private sealed record MenuEnvelope(
        List<MenuItemDto> Itens,
        string TituloPublico,
        string Slug);
}
