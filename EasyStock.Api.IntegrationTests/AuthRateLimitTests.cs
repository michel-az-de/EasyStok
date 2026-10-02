using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// B-015: confirma que a policy "auth" (fixed-window de 20 req/min, particionado por IP) devolve
/// 429 no 21o pedido a uma rota anonima de autenticacao. A prova usa forgot-password: o register
/// anonimo foi removido na N0 (#1349) e o par antigo login/register esperava o bloqueio na 11a
/// chamada, com o balde ja em 20. Roda local, com Docker: este projeto esta fora do EasyStok.CI.slnf.
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

    [SkippableFact]
    public async Task Forgot_password_apos_20_tentativas_no_mesmo_IP_retorna_429()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // E-mail inexistente: a rota responde 200 sem efeito colateral (anti-enumeracao), entao so o
        // permit da janela muda de uma chamada para a outra.
        var payload = new { Email = "naoexiste@easystock.com" };

        // 20 primeiros requests passam: a policy "auth" libera 20 permits por minuto por IP.
        for (var i = 0; i < 20; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", payload);
            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"tentativa {i + 1} ainda dentro da janela de 20 permits/min");
        }

        // 21o request: rate limiter rejeita antes do controller (sem QueueLimit).
        var blocked = await client.PostAsJsonAsync("/api/auth/forgot-password", payload);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.Should().ContainKey("Retry-After");
    }
}
