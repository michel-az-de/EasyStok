using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Storefront.Auth;

/// <summary>
/// Testes de integração do fluxo validar-OTP (EZ-AUTH-002, ADR-0012).
///
/// Requer Docker (Testcontainers) ou env var EASYSTOCK_IT_PG apontando para
/// Postgres externo. Sem nenhum dos dois, os testes retornam sem Assert —
/// não falham como falso-positivo.
/// </summary>
public sealed class ValidarOtpControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;
    private string? _connString;

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    private const string SlugTeste = "test-storefront-validar-otp";
    private const string TelefoneE164 = "+5511987654321";
    private const string CodigoValido = "654321";

    public async Task InitializeAsync()
    {
        var externalPg = Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG");
        if (!string.IsNullOrWhiteSpace(externalPg))
        {
            _connString = externalPg;
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", externalPg);
            Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
            Environment.SetEnvironmentVariable("RunMigrationsOnStartup", "true");
            Environment.SetEnvironmentVariable("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001");
            Environment.SetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD", "Integr4cao-T3st-SuperAdmin");
            _isAvailable = true;
            return;
        }

        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_validarotp_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
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

    private WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _connString,
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = JwtIssuer,
                        ["Jwt:Audience"] = JwtAudience,
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local",
                        ["Mobile:ApiKey"] = "easystock-integration-test-mobile-key-0001",
                    });
                });
            });

    private static async Task SeedDadosAsync(IServiceProvider services, string codigoHash)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        var empresa = Empresa.Criar("Empresa Teste ValidarOtp", null);
        db.Empresas.Add(empresa);

        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: empresa.Id,
            slug: SlugTeste,
            tituloPublico: "Storefront Teste Auth",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);

        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var otp = ClienteOtp.Criar(
            empresaId: empresa.Id,
            telefoneHash: telefoneHash,
            codigoHash: codigoHash,
            time: TimeProvider.System);
        db.ClienteOtps.Add(otp);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ValidarOtp_CodigoCorreto_Retorna200_ComCookieDeSession()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        var codigoHash = BCrypt.Net.BCrypt.HashPassword(CodigoValido);
        await SeedDadosAsync(factory.Services, codigoHash);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeader = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies)
            : string.Empty;
        setCookieHeader.Should().Contain("__Host-cdb_session", "cookie de sessão deve ser setado");
        setCookieHeader.Should().Contain("httponly", because: "cookie deve ter flag HttpOnly");
        setCookieHeader.Should().Contain("samesite=lax", because: "cookie deve ter SameSite=Lax");

        var body = await response.Content.ReadFromJsonAsync<ValidarOtpResponseDto>();
        body.Should().NotBeNull();
        body!.TelefoneOfuscado.Should().Contain("*", because: "telefone deve ser ofuscado na resposta");
        body.PrimeiroNome.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidarOtp_CodigoErrado_Retorna401_SemCookie()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        var codigoHash = BCrypt.Net.BCrypt.HashPassword(CodigoValido);
        await SeedDadosAsync(factory.Services, codigoHash);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = "000000" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("cookie não deve ser setado em erro");
    }

    [Fact]
    public async Task ValidarOtp_StorefrontInexistente_Retorna404()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/storefront/slug-que-nao-existe-xyzxyz/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidarOtp_OtpInexistente_Retorna401()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();

        // Seed apenas storefront, sem OTP
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();
        var empresa = Empresa.Criar("Empresa Sem OTP", null);
        db.Empresas.Add(empresa);
        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresa.Id, SlugTeste + "-sem-otp", "Sem OTP", 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}-sem-otp/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record ValidarOtpResponseDto(string TelefoneOfuscado, string PrimeiroNome);
}
