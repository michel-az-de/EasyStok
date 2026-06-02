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
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Auth;

/// <summary>
/// Integration tests para POST /api/storefront/{slug}/auth/solicitar-otp
/// (TASK-EZ-AUTH-001).
///
/// <para>
/// Sobe Postgres real via Testcontainers, roda migrations, cria Storefront
/// fixture, e faz HTTP request. Em ambientes sem Docker (CI sem privilegio,
/// dev local sem Docker Desktop), tests viram no-op via guarda
/// <c>_isAvailable</c> — segue padrao de <c>AuthRateLimitTests</c>.
/// </para>
/// </summary>
public sealed class AuthControllerTests : IAsyncLifetime
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
                .WithDatabase("easystock_storefront_auth_tests")
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
                b.UseEnvironment("Development"); // Stub do WhatsApp so registra fora de Production
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

    private static async Task<StorefrontEntity> SeedStorefrontAsync(
        WebApplicationFactory<Program> factory,
        string slug,
        Guid? empresaIdOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var storefront = StorefrontEntity.Criar(
            empresaId: empresaIdOverride ?? Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa da Baba (test)",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();

        await storefrontRepo.AddAsync(storefront);
        await uow.CommitAsync();

        return storefront;
    }

    // ── Happy path ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task PostSolicitarOtp_StorefrontValido_Retorna202EPersisteClienteOtp()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontAsync(factory, slug: "casa-da-baba-it1");

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/{storefront.Slug}/auth/solicitar-otp",
            new { telefone = "+5511997573992" });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<SolicitarOtpResponse>();
        body!.ExpiresInSeconds.Should().Be(300);

        // ClienteOtp persistido com hash do telefone
        using var scope = factory.Services.CreateScope();
        var clienteOtpRepo = scope.ServiceProvider.GetRequiredService<IClienteOtpRepository>();
        var telefoneHash = ClienteOtp.CalcularTelefoneHash("+5511997573992");
        var otp = await clienteOtpRepo.GetAtivoPorTelefoneHashAsync(
            storefront.EmpresaId, telefoneHash, DateTime.UtcNow);
        otp.Should().NotBeNull();
        otp!.EmpresaId.Should().Be(storefront.EmpresaId);
        otp.TelefoneHash.Should().Be(telefoneHash);
        otp.CodigoHash.Should().NotBeNullOrEmpty();
        otp.CodigoHash.Should().NotMatch("*\\d{6}*"); // BCrypt hash nunca contem 6 digitos seguidos do codigo plain
    }

    // ── Telefone inválido ──────────────────────────────────────────────

    [SkippableFact]
    public async Task PostSolicitarOtp_TelefoneInvalido_Retorna400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontAsync(factory, slug: "casa-da-baba-it2");

        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/{storefront.Slug}/auth/solicitar-otp",
            new { telefone = "invalido" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Slug inexistente ───────────────────────────────────────────────

    [SkippableFact]
    public async Task PostSolicitarOtp_SlugInexistente_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/storefront/slug-que-nao-existe/auth/solicitar-otp",
            new { telefone = "+5511997573992" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DTO espelho da resposta do Controller ──────────────────────────

    private sealed record SolicitarOtpResponse(int ExpiresInSeconds);
}
