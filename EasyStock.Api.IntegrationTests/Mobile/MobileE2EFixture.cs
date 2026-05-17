using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// Fixture base para testes E2E API↔PWA. Sobe um Postgres real via
/// Testcontainers (skip silencioso quando Docker indisponivel) e cria
/// <see cref="WebApplicationFactory{Program}"/> apontando pra ele.
///
/// Helpers expostos cobrem o fluxo mais comum: provisionar empresa+loja,
/// criar device pareado com api key conhecido, e gerar HttpClient
/// autenticado pra chamar /api/mobile/*.
/// </summary>
public class MobileE2EFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    public bool IsAvailable { get; private set; }
    public WebApplicationFactory<Program>? Factory { get; private set; }

    public const string TestCiKey = "test-ci-key-do-fixture-min-32-chars-please";
    public static readonly Guid OwnerEmpresaId = Guid.Parse("e0e0e0e0-0000-0000-0000-0000000000ce");

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_mobile_e2e")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _pg.StartAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            IsAvailable = false;
            return;
        }

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
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
                        ["Mobile:RequireApiKey"] = "false", // legacy compat off; mas testes que precisam usam header
                        ["Mobile:Pwa:StableCacheVersion"] = "cdb-v3-stable-test",
                        ["Ota:Enabled"] = "true",
                        ["Ci:AutoTicketKey"] = TestCiKey,
                        ["Ci:OwnerEmpresaId"] = OwnerEmpresaId.ToString(),
                    });
                });
            });
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    public HttpClient CreateClient() => Factory!.CreateClient();

    public HttpClient CreateMobileClient(string apiKey)
    {
        var client = Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mobile-Api-Key", apiKey);
        return client;
    }

    public async Task<(Guid empresaId, Guid lojaId)> SeedEmpresaELojaAsync(string? nome = null)
    {
        using var scope = Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = nome ?? "Empresa Teste E2E " + Guid.NewGuid().ToString()[..8],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        db.Empresas.Add(empresa);

        var loja = new Loja
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Loja Teste",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        db.Lojas.Add(loja);

        await db.SaveChangesAsync();
        return (empresa.Id, loja.Id);
    }

    /// <summary>
    /// Cria um MobileDevice pareado com ApiKey conhecido e retorna a apiKey
    /// plaintext. Reusa TokenHashHelper.ComputeSha256Hash pra gerar o hash
    /// armazenado — mesmo caminho que DevicePairingController.
    /// </summary>
    public async Task<MobileDeviceCredentials> SeedMobileDeviceAsync(
        Guid empresaId, Guid lojaId,
        bool isCanary = false, bool revoked = false,
        string? deviceId = null)
    {
        using var scope = Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var apiKey = "tk-" + Guid.NewGuid().ToString("N");
        var apiKeyHash = TokenHashHelper.ComputeSha256Hash(apiKey);

        var device = new MobileDevice
        {
            Id = deviceId ?? ("dev-" + Guid.NewGuid().ToString("N")[..12]),
            ApiKeyHash = apiKeyHash,
            EmpresaId = empresaId,
            LojaId = lojaId,
            Label = "Test Device",
            PairedAt = DateTime.UtcNow,
            IsCanary = isCanary,
            Revoked = revoked,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Set<MobileDevice>().Add(device);
        await db.SaveChangesAsync();

        return new MobileDeviceCredentials(device.Id, apiKey, empresaId, lojaId, isCanary);
    }

    public async Task SeedOwnerEmpresaAsync()
    {
        using var scope = Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        if (await db.Empresas.AnyAsync(e => e.Id == OwnerEmpresaId)) return;
        db.Empresas.Add(new Empresa
        {
            Id = OwnerEmpresaId,
            Nome = "Owner E2E (ci tickets)",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}

public sealed record MobileDeviceCredentials(
    string DeviceId,
    string ApiKey,
    Guid EmpresaId,
    Guid LojaId,
    bool IsCanary);

[CollectionDefinition("MobileE2E")]
public class MobileE2ECollection : ICollectionFixture<MobileE2EFixture> { }
