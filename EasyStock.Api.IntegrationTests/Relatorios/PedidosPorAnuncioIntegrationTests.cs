using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.IntegrationTests.Relatorios;

/// <summary>#2928 (S5): <c>GET api/relatorios/pedidos-por-anuncio</c> com PostgreSQL real e três pedidos (Google, Meta, sem origem).</summary>
public sealed class PedidosPorAnuncioIntegrationTests : IAsyncLifetime
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid OutraEmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private BancoDeTeste? _pg;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new BancoDeTeste();
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

    [SkippableFact]
    public async Task PedidosPorAnuncio_AgrupaPorPlataformaECampanhaComLinhaSemOrigem()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);

        var hoje = HorarioBrasil.Hoje().ToString("yyyy-MM-dd");
        using var resposta = await client.GetAsync($"/api/relatorios/pedidos-por-anuncio?de={hoje}&ate={hoje}");

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        using var json = JsonDocument.Parse(corpo);
        var linhas = json.RootElement.GetProperty("data").EnumerateArray()
            .Select(l => (
                Plataforma: l.GetProperty("plataforma").GetString(),
                Campanha: l.GetProperty("campanha").GetString(),
                Pedidos: l.GetProperty("totalPedidos").GetInt32(),
                Valor: l.GetProperty("valorTotal").GetDecimal(),
                Ticket: l.GetProperty("ticketMedio").GetDecimal()))
            .ToList();

        linhas.Should().Equal(
            ("google", "black-friday", 2, 300m, 150m),
            ("meta", "Promo do brigadeiro", 1, 80m, 80m),
            ("sem_origem", "Sem origem de anúncio", 1, 50m, 50m));
    }

    [SkippableFact]
    public async Task PedidosPorAnuncio_PeriodoSemPedidos_DevolveListaVazia()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);

        using var resposta = await client.GetAsync("/api/relatorios/pedidos-por-anuncio?de=2020-01-01&ate=2020-01-31");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    private static async Task SemearAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var agora = DateTime.UtcNow;
        db.SetMobileTenantContext(EmpresaId);

        foreach (var id in new[] { EmpresaId, OutraEmpresaId })
            db.Empresas.Add(new Empresa { Id = id, Nome = "Empresa " + id.ToString("N")[..8], Documento = id.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });

        // Owned type do EF não se compartilha entre donos: uma instância por pedido.
        OrigemAnuncio google() => OrigemAnuncio.DoSite(
            new OrigemAnuncio.Utms("google", "cpc", "black-friday", null, null),
            new OrigemAnuncio.IdsDeClique(null, null, null, null), OrigemAnuncio.ViaSite, false, agora)!;
        OrigemAnuncio meta() => OrigemAnuncio.DoAnuncioWhatsApp("120001", "Promo do brigadeiro", null, agora)!;

        db.Pedidos.AddRange(
            NovoPedido(EmpresaId, 100m, google()),
            NovoPedido(EmpresaId, 200m, google()),
            NovoPedido(EmpresaId, 80m, meta()),
            NovoPedido(EmpresaId, 50m, null),
            NovoPedido(EmpresaId, 999m, google(), StatusPedidoMapper.Cancelado),
            NovoPedido(EmpresaId, 999m, meta(), StatusPedidoMapper.Rascunho),
            NovoPedido(OutraEmpresaId, 777m, google()));
        await db.SaveChangesAsync();
    }

    private static Pedido NovoPedido(Guid empresaId, decimal total, OrigemAnuncio? origem, string? status = null)
    {
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Item", Quantidade = 1,
            PrecoUnitario = total, Subtotal = total, CriadoEm = DateTime.UtcNow,
        });
        pedido.RecalcularTotal();
        pedido.DefinirOrigemAnuncio(origem);
        if (status is not null) pedido.Status = status;
        return pedido;
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Postgres test container indisponível.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("ConnectionStrings:DefaultConnection", _pg!.GetConnectionString());
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
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICurrentUserAccessor));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddSingleton<ICurrentUserAccessor>(new GerenteStub());

                    services
                        .AddAuthentication(opts =>
                        {
                            opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                            opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                });
            });
    }

    private sealed class GerenteStub : ICurrentUserAccessor
    {
        public Guid EmpresaId => PedidosPorAnuncioIntegrationTests.EmpresaId;
        public bool IsAuthenticated => true;
        public Guid UsuarioId => PedidosPorAnuncioIntegrationTests.UsuarioId;
        public NivelAcesso Nivel => NivelAcesso.Gerente;
        public bool TemPermissao(Permissao permissao) => true;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICurrentUserAccessor currentUser)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UsuarioId.ToString()),
                new Claim(ClaimTypes.Name, "Gerente"),
                new Claim("sub", UsuarioId.ToString()),
                new Claim("empresaId", currentUser.EmpresaId.ToString()),
                new Claim("nivel", currentUser.Nivel.ToString()),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
