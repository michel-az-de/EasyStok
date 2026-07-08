using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Npgsql;

namespace EasyStock.Api.IntegrationTests.Banners;

/// <summary>
/// E2E HTTP dos banners de plataforma (#869): SuperAdmin cadastra -> usuário vê no Web ->
/// confirma/dispensa -> some. Sobe API real (WebApplicationFactory) + Postgres real
/// (Testcontainers) + FileStorage Local. Cobre positivos, prova do P0 (confirmação estável ao
/// trocar de empresa ativa) e negativos/segurança (403/400/404/401/409).
/// </summary>
public sealed class BannerE2ETests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    private static readonly byte[] PngValido = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public async Task InitializeAsync()
    {
        try
        {
            // Senha NÃO-default de propósito: o StartupHardening da API recusa subir com
            // Username=postgres + Password=postgres (credencial default). Com senha própria,
            // o app sobe apontando para o container do Testcontainers.
            _pg = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("easystock_banner_e2e_tests")
                .WithUsername("postgres")
                .WithPassword("e2e_pg_secret_pwd")
                .Build();
            await _pg.StartAsync();
            await AguardarPostgresProntoAsync(_pg.GetConnectionString());
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    /// <summary>
    /// Abre conexões até o Postgres aceitar, ANTES de a app subir — o
    /// <c>DatabaseProviderResolver</c> faz um probe com timeout de 3s no startup e falharia
    /// se o container ainda estivesse esquentando.
    /// </summary>
    private static async Task AguardarPostgresProntoAsync(string connectionString)
    {
        for (var tentativa = 0; tentativa < 30; tentativa++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    // ── Positivos ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Admin_cria_banner_imagem_usuario_ve_confirma_e_some()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Promo",
            tipo = "imagem",
            imagemStorageKey = "banners/promo.png",
            imagemUrl = "https://cdn.easystok.com/banners/promo.png",
            ativo = true,
            exigeConfirmacao = true,
            tamanhoModo = "herdado",
        });

        var usuario = Guid.NewGuid();
        using var user = ClienteUsuario(factory, usuario);

        (await AtivosIdsAsync(user)).Should().Contain(bannerId);

        (await user.PostAsync($"/api/banners/{bannerId}/confirmar", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await AtivosIdsAsync(user)).Should().NotContain(bannerId);
    }

    [SkippableFact]
    public async Task Confirma_em_empresa_A_troca_para_B_obrigatorio_nao_reaparece()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Obrigatório",
            tipo = "mensagem",
            corpo = "Leia e confirme.",
            ativo = true,
            exigeConfirmacao = true,
            tamanhoModo = "herdado",
        });

        var usuario = Guid.NewGuid();

        using (var sobA = ClienteUsuario(factory, usuario, Guid.NewGuid()))
        {
            (await AtivosIdsAsync(sobA)).Should().Contain(bannerId);
            (await sobA.PostAsync($"/api/banners/{bannerId}/confirmar", null)).StatusCode
                .Should().Be(HttpStatusCode.NoContent);
        }

        // Mesmo usuário, empresa ativa diferente (novo token): NÃO deve reaparecer.
        using var sobB = ClienteUsuario(factory, usuario, Guid.NewGuid());
        (await AtivosIdsAsync(sobB)).Should().NotContain(bannerId, "confirmação é keyed por usuário (fix P0)");
    }

    [SkippableFact]
    public async Task Banner_so_texto_aparece_como_mensagem()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Aviso",
            tipo = "mensagem",
            corpo = "Manutenção domingo.",
            ativo = true,
            tamanhoModo = "herdado",
        });

        using var user = ClienteUsuario(factory, Guid.NewGuid());
        var ativos = await AtivosAsync(user);
        ativos.Should().ContainSingle(b => b.Id == bannerId).Which.Tipo.Should().Be("mensagem");
    }

    [SkippableFact]
    public async Task Visualizacao_unica_apos_visto_nao_retorna()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Única",
            tipo = "imagem",
            imagemStorageKey = "banners/unica.png",
            imagemUrl = "https://cdn.easystok.com/banners/unica.png",
            ativo = true,
            visualizacaoUnica = true,
            tamanhoModo = "herdado",
        });

        using var user = ClienteUsuario(factory, Guid.NewGuid());
        (await AtivosIdsAsync(user)).Should().Contain(bannerId);

        (await user.PostAsync($"/api/banners/{bannerId}/visto", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await AtivosIdsAsync(user)).Should().NotContain(bannerId);
    }

    [SkippableFact]
    public async Task Confirmar_duas_vezes_e_idempotente()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Idem",
            tipo = "mensagem",
            corpo = "x",
            ativo = true,
            exigeConfirmacao = true,
            tamanhoModo = "herdado",
        });

        var usuario = Guid.NewGuid();
        using var user = ClienteUsuario(factory, usuario);

        (await user.PostAsync($"/api/banners/{bannerId}/confirmar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await user.PostAsync($"/api/banners/{bannerId}/confirmar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();
        (await db.BannerConfirmacoes.CountAsync(c => c.BannerId == bannerId)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Upload_de_imagem_retorna_storage_key_e_url()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var admin = ClienteSuperAdmin(factory);
        using var form = new MultipartFormDataContent();
        var img = new ByteArrayContent(PngValido);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(img, "file", "banner.png");

        var resp = await admin.PostAsync("/api/admin/banners/imagem", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await resp.Content.ReadFromJsonAsync<UploadEnvelope>();
        env!.Data.StorageKey.Should().StartWith("banners/");
        env.Data.Url.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Banner_com_notificar_persiste_flag()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Com notif",
            tipo = "mensagem",
            corpo = "x",
            ativo = true,
            notificarAoPublicar = true,
            tamanhoModo = "herdado",
        });

        using var admin = ClienteSuperAdmin(factory);
        var resp = await admin.GetAsync($"/api/admin/banners/{bannerId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await resp.Content.ReadFromJsonAsync<AdminEnvelope>();
        env!.Data.NotificarAoPublicar.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Banner_inativo_nao_aparece_para_usuario()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Inativo",
            tipo = "mensagem",
            corpo = "x",
            ativo = false,
            tamanhoModo = "herdado",
        });

        using var user = ClienteUsuario(factory, Guid.NewGuid());
        (await AtivosIdsAsync(user)).Should().NotContain(bannerId);
    }

    // ── Negativos / segurança ───────────────────────────────────────────────

    [SkippableFact]
    public async Task Usuario_comum_nao_pode_criar_banner_403()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var user = ClienteUsuario(factory, Guid.NewGuid());
        var resp = await user.PostAsJsonAsync("/api/admin/banners", new
        {
            tituloInterno = "x",
            tipo = "mensagem",
            corpo = "x",
            tamanhoModo = "herdado",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Payload_invalido_sem_imagem_e_sem_texto_400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var admin = ClienteSuperAdmin(factory);
        var resp = await admin.PostAsJsonAsync("/api/admin/banners", new
        {
            tituloInterno = "Sem conteúdo",
            tipo = "mensagem", // mensagem exige corpo, que não vem
            tamanhoModo = "herdado",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Url_javascript_no_cadastro_400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var admin = ClienteSuperAdmin(factory);
        var resp = await admin.PostAsJsonAsync("/api/admin/banners", new
        {
            tituloInterno = "XSS",
            tipo = "imagem",
            imagemStorageKey = "banners/x.png",
            imagemUrl = "https://cdn/x.png",
            linkAtivo = true,
            linkUrl = "javascript:alert(1)",
            tamanhoModo = "herdado",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Put_reintroduz_url_javascript_400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "Válido",
            tipo = "imagem",
            imagemStorageKey = "banners/x.png",
            imagemUrl = "https://cdn/x.png",
            ativo = true,
            tamanhoModo = "herdado",
        });

        using var admin = ClienteSuperAdmin(factory);
        var resp = await admin.PutAsJsonAsync($"/api/admin/banners/{bannerId}", new
        {
            tituloInterno = "Válido",
            tipo = "imagem",
            imagemStorageKey = "banners/x.png",
            imagemUrl = "https://cdn/x.png",
            linkAtivo = true,
            linkUrl = "javascript:alert(1)",
            tamanhoModo = "herdado",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Upload_arquivo_nao_imagem_400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var admin = ClienteSuperAdmin(factory);
        using var form = new MultipartFormDataContent();
        var fake = new ByteArrayContent(Encoding.UTF8.GetBytes("nao e imagem"));
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fake, "file", "fake.png");

        var resp = await admin.PostAsync("/api/admin/banners/imagem", form);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Confirmar_banner_inexistente_404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var user = ClienteUsuario(factory, Guid.NewGuid());
        var resp = await user.PostAsync($"/api/banners/{Guid.NewGuid()}/confirmar", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Ativos_sem_token_401()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        using var client = factory.CreateClient(); // sem Authorization
        (await client.GetAsync("/api/banners/ativos")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Delete_com_confirmacoes_409()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();

        var bannerId = await CriarBannerAsync(factory, new
        {
            tituloInterno = "A excluir",
            tipo = "mensagem",
            corpo = "x",
            ativo = true,
            exigeConfirmacao = true,
            tamanhoModo = "herdado",
        });

        using (var user = ClienteUsuario(factory, Guid.NewGuid()))
            (await user.PostAsync($"/api/banners/{bannerId}/confirmar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var admin = ClienteSuperAdmin(factory);
        (await admin.DeleteAsync($"/api/admin/banners/{bannerId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Container PostgreSQL indisponível.");

        // Env vars vencem o appsettings.Development.json (precedência do host builder). O
        // ConfigureAppConfiguration in-memory sozinho seria sobrescrito pelo appsettings —
        // por isso o connection string vai por env, como no PostgresApiIntegrationTests.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _pg!.GetConnectionString());
        Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
        Environment.SetEnvironmentVariable("RunMigrationsOnStartup", "true");
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtSecret);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("FileStorage__Provider", "Local");
        Environment.SetEnvironmentVariable("Anthropic__Enabled", "false");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
    }

    private HttpClient ClienteSuperAdmin(WebApplicationFactory<Program> f)
        => ComToken(f.CreateClient(), GerarJwt("SuperAdmin"));

    private HttpClient ClienteUsuario(WebApplicationFactory<Program> f, Guid usuarioId, Guid? empresaId = null)
        => ComToken(f.CreateClient(), GerarJwt("Operador", empresaId ?? Guid.NewGuid(), usuarioId));

    private static HttpClient ComToken(HttpClient c, string jwt)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    private async Task<Guid> CriarBannerAsync(WebApplicationFactory<Program> factory, object payload)
    {
        using var admin = ClienteSuperAdmin(factory);
        var resp = await admin.PostAsJsonAsync("/api/admin/banners", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "SuperAdmin cria banner válido");
        var env = await resp.Content.ReadFromJsonAsync<CriarEnvelope>();
        return env!.Data.BannerId;
    }

    private static async Task<List<BannerAtivoDto>> AtivosAsync(HttpClient user)
    {
        var resp = await user.GetAsync("/api/banners/ativos");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await resp.Content.ReadFromJsonAsync<AtivosEnvelope>();
        return env!.Data;
    }

    private static async Task<List<Guid>> AtivosIdsAsync(HttpClient user)
        => (await AtivosAsync(user)).Select(b => b.Id).ToList();

    private static string GerarJwt(string nivel, Guid? empresaId = null, Guid? usuarioId = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", (usuarioId ?? Guid.NewGuid()).ToString()),
            new("nivel", nivel),
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresaId", empresaId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer, audience: JwtAudience, claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record CriarEnvelope(CriarData Data);
    private sealed record CriarData(Guid BannerId);
    private sealed record AtivosEnvelope(List<BannerAtivoDto> Data);
    private sealed record BannerAtivoDto(Guid Id, string Tipo, string? Corpo);
    private sealed record UploadEnvelope(UploadData Data);
    private sealed record UploadData(string StorageKey, string Url);
    private sealed record AdminEnvelope(AdminData Data);
    private sealed record AdminData(bool NotificarAoPublicar);
}
