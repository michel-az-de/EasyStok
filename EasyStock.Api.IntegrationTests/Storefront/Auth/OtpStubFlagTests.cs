using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Auth;

/// <summary>
/// Issue #677 — flag Otp:UseStub destrava o StubWhatsAppOtpSender em
/// Production enquanto Meta Business Verification (TASK-HUM-001) nao sai.
/// Sem a flag e sem provider real, solicitar-otp informa indisponibilidade; logout continua funcional.
/// </summary>
public sealed class OtpStubFlagTests : IAsyncLifetime
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
                .WithDatabase("easystock_otp_stub_flag_tests")
                .WithUsername("postgres")
                .WithPassword("Si6IT-" + Guid.NewGuid().ToString("N"))
                .Build();

            await _pg.StartAsync();
            await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseNpgsql(_pg.GetConnectionString()).Options);
            using var bypass = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
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

    private WebApplicationFactory<Program> CriarFactory(string environment, bool? useStub)
    {
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL nao disponivel.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("ConnectionStrings:DefaultConnection", _pg!.GetConnectionString());
                if (useStub.HasValue) b.UseSetting("Otp:UseStub", useStub.Value ? "true" : "false");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    var dict = new Dictionary<string, string?>
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
                        ["Mobile:ApiKey"] = "easystock-integration-test-mobile-key-0001",
                        ["RunMigrationsOnStartup"] = "false",
                    };
                    if (useStub.HasValue)
                        dict["Otp:UseStub"] = useStub.Value ? "true" : "false";
                    cfg.AddInMemoryCollection(dict);
                });
            });
    }

    private static async Task<StorefrontEntity> SeedStorefrontAsync(
        WebApplicationFactory<Program> factory,
        string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        var empresa = Empresa.Criar("Empresa OTP test", null);
        db.Empresas.Add(empresa);
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var storefront = StorefrontEntity.Criar(
            empresaId: empresa.Id,
            slug: slug,
            tituloPublico: "Casa da Baba (test)",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();

        await storefrontRepo.AddAsync(storefront);
        await uow.CommitAsync();

        return storefront;
    }

    [SkippableFact]
    public async Task Production_SemProvider_RecusaOtpSemPersistir_MasLogoutFunciona()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(environment: "Production", useStub: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var loja = await SeedStorefrontAsync(factory, "loja-sem-provider-otp");
        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IWhatsAppOtpSender>().Should().BeOfType<IndisponivelWhatsAppOtpSender>();
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            var response = await client.PostAsJsonAsync($"/api/storefront/{loja.Slug}/auth/solicitar-otp",
                new { telefone = "+5511987654321" });
            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            (await response.Content.ReadAsStringAsync()).Should().Contain("OTP_PROVIDER_UNAVAILABLE");
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            (await db.ClienteOtps.CountAsync(o => o.EmpresaId == loja.EmpresaId)).Should().Be(0);
        }
        var csrf = await client.GetAsync($"/api/storefront/{loja.Slug}/auth/csrf");
        csrf.StatusCode.Should().Be(HttpStatusCode.OK);
        csrf.Headers.CacheControl!.NoStore.Should().BeTrue();
        csrf.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-cdb_csrf=", StringComparison.Ordinal))
            .ToLowerInvariant().Should().Contain("secure").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/");
        var token = (await csrf.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("requestToken").GetString();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", token);
        var logout = await client.PostAsync($"/api/storefront/{loja.Slug}/auth/logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SkippableFact]
    public void Production_ComOtpUseStubTrue_RegistraStubEResolve()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        using var factory = CriarFactory(environment: "Production", useStub: true);
        using var scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppOtpSender>();
        sender.Should().BeOfType<StubWhatsAppOtpSender>();
    }

    [SkippableFact]
    public async Task Production_ComOtpUseStubTrue_SolicitarOtpRetorna202()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory(environment: "Production", useStub: true);
        using var client = factory.CreateClient();

        var storefront = await SeedStorefrontAsync(factory, slug: "casa-da-baba-otp-flag");
        var resp = await client.PostAsJsonAsync(
            $"/api/storefront/{storefront.Slug}/auth/solicitar-otp",
            new { telefone = "+5511997573992" });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [SkippableFact]
    public void Development_SemFlag_RegistraStubPorDefault()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        using var factory = CriarFactory(environment: "Development", useStub: null);
        using var scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppOtpSender>();
        sender.Should().BeOfType<StubWhatsAppOtpSender>();
    }
}
