using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// B-015: confirma que /api/auth/login e /api/auth/register estao cobertos pela
/// policy "auth" (fixed-window 10 req/min particionado por IP). Brute-force e spam
/// de tenant disparam 429 antes da 11a tentativa por IP.
/// </summary>
public sealed class AuthRateLimitTests : IAsyncLifetime
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
                .WithDatabase("easystock_ratelimit_tests")
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
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = JwtIssuer,
                        ["Jwt:Audience"] = JwtAudience,
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local"
                    });
                });
            });
    }

    [Fact]
    public async Task Login_apos_10_tentativas_invalidas_no_mesmo_IP_retorna_429()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "naoexiste@easystock.com", Senha = "Errada@123!" };

        // 10 primeiros requests devolvem 401 (credenciais invalidas) — todos consomem o permit.
        for (var i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login", payload);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"tentativa {i + 1} ainda dentro da janela de 10 permits/min");
        }

        // 11o request: rate limiter rejeita antes do controller (sem QueueLimit).
        var blocked = await client.PostAsJsonAsync("/api/auth/login", payload);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task Register_apos_10_tentativas_no_mesmo_IP_retorna_429()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // Payload propositalmente invalido — interessa o COUNTING, nao o resultado de negocio.
        var payload = new
        {
            Nome = "x",
            Email = "spam@spam.com",
            Senha = "x",
            EmpresaId = (Guid?)null
        };

        for (var i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/register", payload);
            resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"tentativa {i + 1} ainda dentro da janela");
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/register", payload);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.Should().ContainKey("Retry-After");
    }
}
