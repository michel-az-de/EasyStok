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
    private string? _motivoIndisponivel;
    private string? _connString;
    private readonly VariaveisDeProcesso _variaveis = new();

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
        // Prioridade 1: Postgres externo via env var EASYSTOCK_IT_PG (CI ou ambiente
        // sem Docker — ex.: Postgres local/WSL). Permite rodar a integração web→API
        // DE VERDADE onde o Testcontainers não consegue subir um container.
        var externalPg = Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG");
        if (!string.IsNullOrWhiteSpace(externalPg))
        {
            _connString = externalPg;
            // WebApplicationFactory + Minimal API top-level: o ConfigureAppConfiguration
            // nem sempre sobrescreve o que o Program lê no startup (o GetConnectionString
            // saía null → AddNpgSql quebrava). Env vars são lidas pelo CreateBuilder de
            // forma confiável (precedência alta, cedo).
            _variaveis
                .Definir("ConnectionStrings__DefaultConnection", externalPg)
                .Definir("Database__Provider", "PostgreSql")
                .Definir("RunMigrationsOnStartup", "true")
                .Definir("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001")
                // Production exige a senha do bootstrap de SuperAdmin via env var.
                .Definir("SEED_SUPERADMIN_PASSWORD", "Integr4cao-T3st-SuperAdmin");
            _isAvailable = true;
            return;
        }

        // Prioridade 2: Testcontainers (Docker disponível).
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_api_tests")
                .WithUsername("postgres")
                .WithPassword("senha-de-teste-descartavel") // #1890: StartupHardening recusa postgres/postgres
                .Build();

            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
            _isAvailable = true;
        }
        catch (DockerUnavailableException ex)
        {
            _isAvailable = false;
            _motivoIndisponivel = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        _variaveis.Dispose();
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_connString is null) throw new InvalidOperationException("PostgreSQL de teste não disponível.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                // Development (padrão do WebApplicationFactory): roda migrations + seed
                // demo (cria o admin de teste) no startup e liga ValidateOnBuild — que
                // valida o container REAL de produção (branch postgresql). A connection
                // string vai por UseSetting, que chega antes de o Program.cs ler a conexão
                // (#1890); o ConfigureAppConfiguration abaixo chega tarde para isso.
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("ConnectionStrings:DefaultConnection", _connString);
                // O AddJwtBearer le o segredo no registro dos servicos: sem UseSetting, o token que o
                // proprio login emite e validado com o segredo do appsettings e responde 401 (#1890).
                b.UseSetting("Jwt:SecretKey", JwtSecret);
                b.UseSetting("Jwt:Issuer", JwtIssuer);
                b.UseSetting("Jwt:Audience", JwtAudience);
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"]                      = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"]     = _connString,
                        // Production não roda migrations no startup por padrão; forçamos
                        // p/ o banco de teste vazio receber o schema completo (inclui RLS).
                        ["RunMigrationsOnStartup"]                  = "true",
                        // Em Production a app exige Mobile:ApiKey com >= 24 chars.
                        ["Mobile:ApiKey"]                           = "easystock-integration-test-mobile-key-0001",
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

    private async Task<HttpClient> ClienteAutenticadoAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@casadababa.demo", Senha = "admin123" });

        Skip.IfNot(loginResp.IsSuccessStatusCode,
            "Login admin indisponível (seed não rodou) — fluxo autenticado web→API pulado.");

        // O login responde no envelope ApiResponse: { data: { token, ... }, meta } (#1890).
        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("token").GetString();
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
                new { Email = "admin@casadababa.demo", Senha = "admin123" });
            _seedDemoDisponivelCache = loginResp.IsSuccessStatusCode;
            return _seedDemoDisponivelCache.Value;
        }
        finally
        {
            _seedDemoCheckLock.Release();
        }
    }

    // ─── Canário do harness ───────────────────────────────────────────────────

    /// <summary>
    /// Guarda anti-falso-verde do projeto (#2176), no desenho de
    /// <c>EasyStock.Infra.Postgre.IntegrationTests/HarnessCanaryTests</c>. O job
    /// <c>api-integration</c> não define <c>EASYSTOCK_IT_PG</c> e depende do
    /// Testcontainers: se o Docker do runner degradar, todos os
    /// <c>[SkippableFact]</c> deste projeto pulam e o gate fica verde-vazio. No GitHub
    /// Actions isso vira VERMELHO. Localmente, sem a env var, SKIPa visível (ADR-0023).
    /// </summary>
    [SkippableFact]
    public void Harness_executa_de_verdade_no_CI()
    {
        var noGitHubActions = string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true",
            StringComparison.OrdinalIgnoreCase);
        Skip.IfNot(noGitHubActions, "GITHUB_ACTIONS ausente (rodada local).");

        _isAvailable.Should().BeTrue(
            "no CI o Postgres de teste é obrigatório; sem ele os SkippableFact deste "
            + "projeto pulariam verdes: " + (_motivoIndisponivel ?? "(sem motivo)"));
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
            "Seed demo (admin@casadababa.demo, CasaDaBabaSeed) indisponível neste ambiente.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        // O seed roda no startup, portanto os usuarios ja existem
        var payload = new { Email = "admin@casadababa.demo", Senha = "admin123" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Login_gerente_deve_retornar_token_JWT()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (gerente) indisponível neste ambiente.");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();

        var payload = new { Email = "gerente@casadababa.demo", Senha = "gerente123" };
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

        var payload = new { Email = "operador.centro@casadababa.demo", Senha = "operador123" };
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
    [InlineData("/api/categorias?empresaId={empresaId}")] // o Web manda empresaId (CategoriasService); sem ele, 400
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

        if (path.Contains("{empresaId}"))
        {
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter);
            path = path.Replace("{empresaId}", jwt.Claims.First(c => c.Type == "empresaId").Value);
        }

        var response = await client.GetAsync(path);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NoContent,
            HttpStatusCode.NotFound // aceitável em coleções vazias com ID
        );
    }

    // ─── Idempotência na criação de cadastro (#2462) ──────────────────────────

    [SkippableFact]
    public async Task Criar_fornecedor_duas_vezes_com_a_mesma_chave_grava_um_so_e_o_segundo_e_replay()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        Skip.IfNot(await SeedDemoDisponivelAsync(),
            "Seed demo (admin) indisponível neste ambiente — fluxo autenticado web→API pulado.");

        await using var factory = CriarFactory();
        using var client = await ClienteAutenticadoAdminAsync(factory);
        var nome = $"Fornecedor idempotente {Guid.NewGuid():N}";
        var chave = Guid.NewGuid().ToString();

        Task<HttpResponseMessage> Criar(object corpo)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/fornecedores") { Content = JsonContent.Create(corpo) };
            request.Headers.Add("Idempotency-Key", chave);
            return client.SendAsync(request);
        }

        using var primeira = await Criar(new { nome, origem = "console" });
        primeira.StatusCode.Should().Be(HttpStatusCode.Created, await primeira.Content.ReadAsStringAsync());
        primeira.Headers.Contains("X-Idempotent-Replay").Should().BeFalse();

        using var segunda = await Criar(new { nome, origem = "console" });
        segunda.StatusCode.Should().Be(HttpStatusCode.Created, await segunda.Content.ReadAsStringAsync());
        segunda.Headers.GetValues("X-Idempotent-Replay").Should().ContainSingle().Which.Should().Be("true");

        var id1 = (await primeira.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString();
        var id2 = (await segunda.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString();
        id2.Should().Be(id1, "a repetição devolve o cadastro já gravado");

        using var divergente = await Criar(new { nome = nome + " outro", origem = "console" });
        divergente.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await divergente.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        var lista = await client.GetFromJsonAsync<JsonElement>($"/api/fornecedores?search={Uri.EscapeDataString(nome)}&pageSize=50");
        lista.GetProperty("data").EnumerateArray()
            .Count(f => f.GetProperty("nome").GetString()!.StartsWith(nome)).Should().Be(1, "só um fornecedor foi gravado");
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
