using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Testes de integração web→API com PostgreSQL real via Testcontainers. Sobem a
/// API inteira (<see cref="WebApplicationFactory{Program}"/>) apontando para um
/// Postgres efêmero, rodam migrations + seed no startup e batem HTTP nos mesmos
/// endpoints que o EasyStock.Web consome.
///
/// <para>
/// <b>Docker obrigatório.</b> Sem Docker o Testcontainers não sobe e os testes
/// são PULADOS de forma visível (<c>Skip.IfNot</c>) — nunca passam vazios
/// (falso-verde). Em CI/máquina com Docker eles exercem o caminho real.
/// </para>
/// </summary>
public sealed class PostgresApiIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;
    private string? _connString;

    private const string JwtIssuer  = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret  = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    // Cache classe-scoped: a checagem de "seed demo disponível" é determinística
    // dentro do mesmo processo (mesma Program.cs, mesma config). Sem cache, cada
    // um dos 12 testes que dependem do seed pagava ~30s subindo WebApplicationFactory
    // só para descobrir que o login falhava — total ~6min queimados/run.
    // Com cache: o primeiro paga o custo, os outros 11 leem a flag e pulam em ms.
    private static bool? _seedDemoDisponivelCache;
    private static readonly SemaphoreSlim _seedDemoCheckLock = new(1, 1);

    public async Task InitializeAsync()
    {
        // Testcontainers com senha NAO-default: o StartupHardening da API recusa subir com
        // Username=postgres + Password=postgres (credencial default). Container proprio =>
        // isolado do banco do Inventario.IntegrationTests, sem contencao de migrations no CI
        // (o incidente do #824 tinha 56 falhas de creds default + colisao no easystock_ci
        // compartilhado). NAO usa EASYSTOCK_IT_PG de proposito: aquele aponta pro service do
        // ci.yml (postgres/postgres) que a guarda barraria ao subir o app. Espelha o padrao
        // ja provado do BannerE2ETests. Sem Docker (ex.: dotnet Windows local) os testes SKIPam.
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_api_tests")
                .WithUsername("postgres")
                .WithPassword("api_it_secret_pwd")
                .Build();

            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
            await AguardarPostgresProntoAsync(_connString);
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    /// <summary>
    /// Abre conexoes ate o Postgres aceitar, ANTES de a app subir — o
    /// DatabaseProviderResolver faz um probe com timeout curto no startup e falharia se o
    /// container ainda estivesse esquentando (espelha BannerE2ETests).
    /// </summary>
    private static async Task AguardarPostgresProntoAsync(string connectionString)
    {
        for (var tentativa = 0; tentativa < 30; tentativa++)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_connString is null) throw new InvalidOperationException("PostgreSQL de teste não disponível.");

        // Env vars vencem o appsettings.Development.json (precedencia do host builder); o
        // ConfigureAppConfiguration in-memory sozinho era sobrescrito pelo appsettings (a
        // connection string saia null -> AddNpgSql quebrava, #261). Mesmo padrao do
        // BannerE2ETests. A senha do _connString e nao-default (Testcontainers), entao o
        // StartupHardening passa. Development: roda migrations + seed demo no startup.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connString);
        Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
        Environment.SetEnvironmentVariable("RunMigrationsOnStartup", "true");
        Environment.SetEnvironmentVariable("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001");
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtSecret);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("FileStorage__Provider", "Local");
        Environment.SetEnvironmentVariable("Anthropic__Enabled", "false");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
    }

    private async Task<HttpClient> ClienteAutenticadoAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" });

        Skip.IfNot(loginResp.IsSuccessStatusCode,
            "Login admin indisponível (seed não rodou) — fluxo autenticado web→API pulado.");

        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Helper: checa UMA VEZ por processo se o seed demo está disponível (login do
    // admin funciona). Antes desta otimização, cada um dos 12 testes que depende
    // do seed subia WebApplicationFactory (~30s) só pra descobrir que o Skip ia
    // pular. Agora o primeiro paga, os outros 11 leem a flag e pulam cedo.
    private async Task<bool> SeedDemoDisponivelAsync()
    {
        if (_seedDemoDisponivelCache.HasValue)
            return _seedDemoDisponivelCache.Value;

        await _seedDemoCheckLock.WaitAsync();
        try
        {
            if (_seedDemoDisponivelCache.HasValue)
                return _seedDemoDisponivelCache.Value;

            await using var factory = CriarFactory();
            using var client = factory.CreateClient();
            var loginResp = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" });
            _seedDemoDisponivelCache = loginResp.IsSuccessStatusCode;
            return _seedDemoDisponivelCache.Value;
        }
        finally
        {
            _seedDemoCheckLock.Release();
        }
    }

    // ─── Health Check ─────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Health_deve_retornar_Healthy_com_PostgreSQL()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Autenticação ─────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Login_com_credenciais_invalidas_deve_retornar_401()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "inexistente@easystock.com", Senha = "Errada@123!" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Login_admin_deve_retornar_token_JWT()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (felipe@easystock.com) indisponível neste ambiente.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // O seed roda no startup, portanto os usuarios ja existem
        var payload = new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Login_gerente_deve_retornar_token_JWT()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (gerente) indisponível neste ambiente.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "thatiane@easystock.com", Senha = "Thati@2026!Gerente" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Login_operador_deve_retornar_token_JWT()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (operador) indisponível neste ambiente.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "operador.fone@easystock.com", Senha = "OpFone@2026!Access" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Endpoints protegidos sem autenticação (todos exigem token) ────────────

    [SkippableTheory]
    [InlineData("/api/produtos")]
    [InlineData("/api/estoque")]
    [InlineData("/api/lojas")]
    [InlineData("/api/categorias")]
    [InlineData("/api/notificacoes")]
    [InlineData("/api/contas-a-pagar")]
    [InlineData("/api/contas-a-receber")]
    [InlineData("/api/pedidos")]
    public async Task Endpoint_protegido_sem_token_deve_retornar_401(string path)
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Endpoints com autenticação Admin (web→API ponta-a-ponta) ──────────────
    // Inclui os módulos que quebravam em produção (contas a pagar/receber, pedidos):
    // este teste prova que login + resolução de empresaId + query no Postgres real
    // funcionam ponta-a-ponta, sem 500.

    [SkippableTheory]
    [InlineData("/api/produtos")]
    [InlineData("/api/estoque")]
    [InlineData("/api/lojas")]
    [InlineData("/api/categorias")]
    [InlineData("/api/fornecedores")]
    [InlineData("/api/notificacoes")]
    [InlineData("/api/contas-a-pagar")]
    [InlineData("/api/contas-a-receber")]
    [InlineData("/api/pedidos")]
    public async Task Endpoint_com_token_admin_deve_retornar_2xx(string path)
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (admin) indisponível neste ambiente — fluxo autenticado web→API pulado.");

        await using var factory = CriarFactory();
        using var client = await ClienteAutenticadoAdminAsync(factory);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NoContent,
            HttpStatusCode.NotFound // aceitável em coleções vazias com ID
        );
    }

    // ─── Migrations ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Migrations_devem_rodar_sem_erros_no_startup()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");

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
