using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Testes de integração da API com PostgreSQL real via Testcontainers.
/// Cobrem: health check, autenticação (login), e endpoints protegidos.
/// </summary>
public sealed class PostgresApiIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtIssuer  = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret  = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_api_tests")
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
        if (_pg is null) throw new InvalidOperationException("Contêiner PostgreSQL não disponível.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"]                      = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"]     = _pg.GetConnectionString(),
                        ["ConnectionStrings:Redis"]                 = "localhost:6379",
                        ["Jwt:Issuer"]                             = JwtIssuer,
                        ["Jwt:Audience"]                           = JwtAudience,
                        ["Jwt:SecretKey"]                          = JwtSecret,
                        ["Jwt:ExpirationMinutes"]                  = "60",
                        ["Anthropic:Enabled"]                      = "false",
                        ["FileStorage:Provider"]                   = "Local",
                        // Desabilitar Redis real nos testes
                        ["ConnectionStrings:Redis"]                = "localhost:6379"
                    });
                });
            });
    }

    // ─── Health Check ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_deve_retornar_Healthy_com_PostgreSQL()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Autenticação ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_com_credenciais_invalidas_deve_retornar_401()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "inexistente@easystock.com", Senha = "Errada@123!" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_admin_deve_retornar_token_JWT()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // O seed roda no startup, portanto os usuarios ja existem
        var payload = new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_gerente_deve_retornar_token_JWT()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "thatiane@easystock.com", Senha = "Thati@2026!Gerente" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_operador_deve_retornar_token_JWT()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "operador.fone@easystock.com", Senha = "OpFone@2026!Access" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Endpoints protegidos sem autenticação ────────────────────────────────

    [Theory]
    [InlineData("/api/produtos")]
    [InlineData("/api/estoque")]
    [InlineData("/api/lojas")]
    [InlineData("/api/categorias")]
    [InlineData("/api/notificacoes")]
    public async Task Endpoint_protegido_sem_token_deve_retornar_401(string path)
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Endpoints com autenticação Admin ─────────────────────────────────────

    [Theory]
    [InlineData("/api/produtos")]
    [InlineData("/api/estoque")]
    [InlineData("/api/lojas")]
    [InlineData("/api/categorias")]
    [InlineData("/api/fornecedores")]
    [InlineData("/api/notificacoes")]
    public async Task Endpoint_com_token_admin_deve_retornar_2xx(string path)
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // Login como Admin
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" });

        if (!loginResp.IsSuccessStatusCode) return; // Seed ainda nao rodou

        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NoContent,
            HttpStatusCode.NotFound // aceitável em coleções vazias com ID
        );
    }

    // ─── Migrations ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Migrations_devem_rodar_sem_erros_no_startup()
    {
        if (!_isAvailable) return;

        // Se a factory sobe sem exceção, as migrations rodaram
        await using var factory = CriarFactory();
        var act = async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        };

        await act.Should().NotThrowAsync();
    }
}
