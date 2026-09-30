using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Pedidos;

/// <summary>
/// Integration tests do caminho GUEST de <c>PedidosClienteController.Obter</c>
/// (issues #684/#854 — link "Acompanhe seu pedido" do checkout guest).
///
/// <para>
/// Sobe Postgres real via Testcontainers e bate em
/// <c>GET /api/storefront/{slug}/pedidos/{pedidoId}?t=&lt;jwt&gt;</c>:
/// </para>
/// <list type="bullet">
///   <item>Token válido + pedido guest — 200 com o DTO.</item>
///   <item>Token de outro pedido / adulterado — 404 (anti-enumeração, nunca 401).</item>
///   <item>Pedido de origem logada + token válido — 404 (token guest não destrava).</item>
///   <item>Sem token e sem cookie — 401 (regressão do caminho logado).</item>
/// </list>
/// </summary>
public sealed class PedidoGuestTrackingControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string Slug = "casa-da-baba-it-guest";
    private const string AcompanhamentoSecret = "EasyStock-Test-Acompanhamento-Min32Chars!!";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_guest_tracking_tests")
                .WithUsername("postgres")
                .WithPassword("EasyStock-IT-NonDefault-2026!")
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
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                        ["Jwt:SecretKey"] = "EasyStock-Test-SuperSecretKey-Min32Chars!!",
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local",
                        ["Acompanhamento:JwtSecret"] = AcompanhamentoSecret,
                    });
                });
            });
    }

    /// <summary>Semeia Empresa + Storefront ativo + Pedido, devolvendo o pedidoId.</summary>
    private static async Task<Guid> SeedPedidoAsync(
        WebApplicationFactory<Program> factory,
        string origem = "storefront-guest")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var empresaId = Guid.NewGuid();
        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Guest Tracking Tests",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // Storefront do slug — um por teste (empresa nova), slug fixo exige
        // idempotência: só cria se ainda não existe.
        var existente = await storefrontRepo.GetBySlugAsync(Slug, CancellationToken.None);
        var storefront = existente;
        if (storefront is null)
        {
            storefront = StorefrontEntity.Criar(
                empresaId: empresaId,
                slug: Slug,
                tituloPublico: "Casa da Baba (it-guest)",
                pedidoMinimoEntrega: 0m);
            storefront.Ativar();
            await storefrontRepo.AddAsync(storefront);
        }

        var pedido = Pedido.Criar(storefront.EmpresaId, origem: origem);
        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;
        pedido.ClienteNome = "Convidada Teste";
        pedido.ClienteTelefone = "11999990000";
        pedido.Total = Dinheiro.FromDecimal(89m);
        await pedidoRepo.AddAsync(pedido);
        await uow.CommitAsync();

        return pedido.Id;
    }

    private static string GerarToken(WebApplicationFactory<Program> factory, Guid pedidoId)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<AcompanhamentoTokenService>()
            .Gerar(pedidoId);
    }

    // ── Testes ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Get_TokenValidoPedidoGuest_Retorna200ComDto()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);
        var token = GerarToken(factory, pedidoId);

        var resp = await client.GetAsync(
            $"/api/storefront/{Slug}/pedidos/{pedidoId}?t={Uri.EscapeDataString(token)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pedido").GetProperty("pedidoId").GetGuid().Should().Be(pedidoId);
        body.GetProperty("pedido").GetProperty("status").GetString()
            .Should().Be("AguardandoAprovacaoBaba");
    }

    [SkippableFact]
    public async Task Get_TokenDeOutroPedido_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);
        var tokenDeOutro = GerarToken(factory, Guid.NewGuid());

        var resp = await client.GetAsync(
            $"/api/storefront/{Slug}/pedidos/{pedidoId}?t={Uri.EscapeDataString(tokenDeOutro)}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Get_TokenAdulterado_Retorna404NaoVaza401()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);
        var token = GerarToken(factory, pedidoId);
        var adulterado = token[..^5] + "XXXXX";

        var resp = await client.GetAsync(
            $"/api/storefront/{Slug}/pedidos/{pedidoId}?t={Uri.EscapeDataString(adulterado)}");

        // Anti-enumeracao: token invalido responde como inexistente.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Get_PedidoLogadoComTokenGuestValido_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // Pedido de cliente logado (origem != storefront-guest): token guest
        // com o ID certeiro NAO destrava.
        var pedidoId = await SeedPedidoAsync(factory, origem: "storefront");
        var token = GerarToken(factory, pedidoId);

        var resp = await client.GetAsync(
            $"/api/storefront/{Slug}/pedidos/{pedidoId}?t={Uri.EscapeDataString(token)}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Get_SemTokenESemCookie_Retorna401()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);

        var resp = await client.GetAsync($"/api/storefront/{Slug}/pedidos/{pedidoId}");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
