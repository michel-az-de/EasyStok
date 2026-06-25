using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Integrations.WhatsApp;
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
/// Sem a flag, Production deve continuar falhando rapido (fail-fast no DI).
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

    private WebApplicationFactory<Program> CriarFactory(string environment, bool? useStub)
    {
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL nao disponivel.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
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
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var storefront = StorefrontEntity.Criar(
            empresaId: Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa da Baba (test)",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();

        await storefrontRepo.AddAsync(storefront);
        await uow.CommitAsync();

        return storefront;
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
