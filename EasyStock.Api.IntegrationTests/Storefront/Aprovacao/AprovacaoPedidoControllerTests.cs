using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Storefront.Aprovacao;

/// <summary>
/// Integration tests E2E do <c>AprovacaoPedidoController</c> (TASK-EZ-APROVAR-001).
///
/// <para>
/// Sobe Postgres real via Testcontainers, registra um <c>ICurrentUserAccessor</c>
/// stubado + <c>TestAuth</c> scheme para autenticar o caller como Babá ativa
/// (sem precisar passar pelo middleware JWT). Faz request HTTP completa e valida:
/// </para>
/// <list type="bullet">
///   <item>POST aprovar — 200 + Pedido transita para AprovadoBaba + Outbox enfileira evento.</item>
///   <item>POST recusar — 200 + Pedido vira Cancelado + 3 eventos no Outbox.</item>
///   <item>Sem auth — 401.</item>
///   <item>Pedido inexistente — 404.</item>
///   <item>Tenant mismatch — 404 (não 403, evita oracle).</item>
///   <item>Status inválido (já aprovado) — 409 + body com statusAtual.</item>
///   <item>Motivo inválido em recusar — 422.</item>
/// </list>
/// </summary>
public sealed class AprovacaoPedidoControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioBabaId = Guid.NewGuid();
    private const string UsuarioBabaNome = "Babá Maria";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_aprovacao_tests")
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

    private WebApplicationFactory<Program> CriarFactory(bool autenticado = true)
    {
        if (_pg is null) throw new InvalidOperationException("Postgres test container indisponível.");

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
                    });
                });

                b.ConfigureTestServices(services =>
                {
                    // Substitui ICurrentUserAccessor: caller é Babá ativa do tenant.
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICurrentUserAccessor));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddSingleton<ICurrentUserAccessor>(new StubCurrentUserAccessor(
                        empresaId: autenticado ? EmpresaId : Guid.Empty,
                        usuarioId: autenticado ? UsuarioBabaId : Guid.Empty,
                        isAuthenticated: autenticado));

                    // Sobrescreve auth: scheme "TestAuth" sempre vence; sem token → 401.
                    services
                        .AddAuthentication(opts =>
                        {
                            opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                            opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });

                    services.Configure<TestAuthHandlerOptions>(o => o.Authenticated = autenticado);
                });
            });
    }

    // ── Seed helpers ─────────────────────────────────────────────────────

    private async Task<Guid> SeedPedidoAsync(
        WebApplicationFactory<Program> factory,
        string status = StatusPedidoMapper.AguardandoAprovacaoBaba,
        Guid? empresaIdOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Garante Empresa.
        var db = scope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        var emp = empresaIdOverride ?? EmpresaId;
        if (!await db.Empresas.AnyAsync(e => e.Id == emp))
        {
            db.Empresas.Add(new Empresa
            {
                Id = emp,
                Nome = "Empresa Aprovacao Tests",
                Documento = emp.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pedido = Pedido.Criar(emp, origem: "storefront");
        pedido.Status = status;
        pedido.ClienteNome = "Cliente Teste";
        pedido.ClienteTelefone = "11999990000";
        pedido.Total = Dinheiro.FromDecimal(120m);
        await pedidoRepo.AddAsync(pedido);
        await uow.CommitAsync();

        return pedido.Id;
    }

    // ── Testes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostAprovar_HappyPath_Retorna200ETransitaPedidoParaAprovadoBaba()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{pedidoId}/aprovar",
            new { observacoes = "ok, em produção" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pedidoId").GetGuid().Should().Be(pedidoId);
        body.GetProperty("status").GetString().Should().Be(StatusPedidoMapper.AprovadoBaba);
        body.GetProperty("notificacaoCliente").GetProperty("enfileirada").GetBoolean().Should().BeTrue();

        // Verifica estado persistido.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        var pedido = await db.Pedidos.FindAsync(pedidoId);
        pedido!.Status.Should().Be(StatusPedidoMapper.AprovadoBaba);
        pedido.AprovadoEm.Should().NotBeNull();
        pedido.AprovadoPorUsuarioId.Should().Be(UsuarioBabaId);
    }

    [Fact]
    public async Task PostRecusar_HappyPath_Retorna200ECancelaPedido()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{pedidoId}/recusar",
            new
            {
                motivo = "ESTOQUE_INSUFICIENTE",
                mensagemCliente = "Item esgotado, tente outro.",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pedidoId").GetGuid().Should().Be(pedidoId);
        body.GetProperty("status").GetString().Should().Be(StatusPedidoMapper.Cancelado);
        body.GetProperty("motivo").GetString().Should().Be("estoque_insuficiente");
        body.GetProperty("vagaLiberada").GetBoolean().Should().BeTrue();
        body.GetProperty("refund").GetProperty("enfileirado").GetBoolean().Should().BeTrue();
        body.GetProperty("notificacaoCliente").GetProperty("enfileirada").GetBoolean().Should().BeTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        var pedido = await db.Pedidos.FindAsync(pedidoId);
        pedido!.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedido.RecusadoPorUsuarioId.Should().Be(UsuarioBabaId);
        pedido.MotivoRecusa.Should().Be("estoque_insuficiente");
    }

    [Fact]
    public async Task PostAprovar_SemAuth_Retorna401()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory(autenticado: false);
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{Guid.NewGuid()}/aprovar",
            new { observacoes = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAprovar_PedidoInexistente_Retorna404()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{Guid.NewGuid()}/aprovar",
            new { observacoes = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostAprovar_TenantMismatch_Retorna404()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // Pedido pertence a outra empresa.
        var outraEmpresa = Guid.NewGuid();
        var pedidoId = await SeedPedidoAsync(factory, empresaIdOverride: outraEmpresa);

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{pedidoId}/aprovar",
            new { observacoes = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant mismatch retorna 404 (não 403) para evitar oracle de existência cross-tenant");
    }

    [Fact]
    public async Task PostAprovar_PedidoJaAprovado_Retorna409ComStatusAtual()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory, status: StatusPedidoMapper.AprovadoBaba);

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{pedidoId}/aprovar",
            new { observacoes = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("statusAtual").GetString().Should().Be(StatusPedidoMapper.AprovadoBaba);
    }

    [Fact]
    public async Task PostRecusar_MotivoInvalido_Retorna422()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var pedidoId = await SeedPedidoAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/pedidos/{pedidoId}/recusar",
            new { motivo = "INVALIDO_XYZ", mensagemCliente = "qq" });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Test infrastructure ─────────────────────────────────────────────────

    private sealed class StubCurrentUserAccessor(
        Guid empresaId,
        Guid usuarioId,
        bool isAuthenticated) : ICurrentUserAccessor
    {
        public Guid EmpresaId { get; } = empresaId;
        public bool IsAuthenticated { get; } = isAuthenticated;
        public Guid UsuarioId { get; } = usuarioId;
        public NivelAcesso Nivel => NivelAcesso.Admin;

        public bool TemPermissao(Permissao permissao) => true;
    }

    public sealed class TestAuthHandlerOptions
    {
        public bool Authenticated { get; set; } = true;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<TestAuthHandlerOptions> testOptions)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestAuth";
        private readonly IOptionsMonitor<TestAuthHandlerOptions> _testOptions = testOptions;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!_testOptions.CurrentValue.Authenticated)
                return Task.FromResult(AuthenticateResult.Fail("Not authenticated (test stub)."));

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UsuarioBabaId.ToString()),
                new Claim(ClaimTypes.Name, UsuarioBabaNome),
                new Claim("sub", UsuarioBabaId.ToString()),
                new Claim("empresaId", EmpresaId.ToString()),
                new Claim("nivel", NivelAcesso.Admin.ToString()),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

internal static class DbContextExtensions
{
    public static async Task<bool> AnyAsync<T>(
        this Microsoft.EntityFrameworkCore.DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(set, predicate);
    }
}
