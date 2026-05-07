using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// Stress + smoke do fluxo OTA do PWA controlado pelo backend (Onda 9).
///
/// Cobre o circuito completo:
///   1. <c>GET /api/mobile/version</c> retorna <c>Ota.PwaCacheVersion</c> lido
///      do <c>sw.js</c> servido (PwaVersionProvider). PWA usa pra detectar update.
///   2. Pareamento — admin gera pair-code, "device" troca por apiKey.
///   3. Comandos individuais: pwa_update e clear_cache aceitos; bogus rejeita.
///   4. Pull: device pega seus comandos pendentes (e só os seus).
///   5. Broadcast: 1 chamada enfileira pra N devices duma empresa/loja.
///   6. Filtros: revogados, pendentes (<c>pairing_code != null</c>) e
///      cross-tenant excluídos do broadcast.
///   7. Stress: 50 devices em 1 broadcast + 10 broadcasts paralelos pra garantir
///      que o EF não corrompe estado sob carga.
///
/// Usa o tenant CasaDaBaba do seed (admin@casadababa.demo). Cada teste cria
/// seus próprios devices com IDs únicos pra ficar isolado.
/// </summary>
public sealed class MobileOtaIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private WebApplicationFactory<Program>? _factory;
    private bool _isAvailable;
    private TenantContext? _tenant;

    private const string JwtIssuer  = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret  = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    // Seed do CasaDaBaba garante que esses dados existam após startup.
    // Ver EasyStock.Api/Data/Tenants/CasaDaBabaSeed.cs
    private const string AdminEmail   = "admin@casadababa.demo";
    private const string AdminSenha   = "admin123";
    private const string CasaDocumento = "48.735.219/0001-62";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_ota_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _pg.StartAsync();
            _isAvailable = true;

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    // Production pula o auto-detect de provider (que faz check de
                    // 3s no Postgres; em CI lento ele expira e cai em SQLite, que
                    // NÃO registra os repositórios novos — quebrando o DI).
                    b.UseEnvironment("Production");
                    b.ConfigureAppConfiguration((_, cfg) =>
                    {
                        cfg.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Database:Provider"]                  = "PostgreSql",
                            ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
                            ["ConnectionStrings:Redis"]             = "localhost:6379",
                            ["Jwt:Issuer"]                         = JwtIssuer,
                            ["Jwt:Audience"]                       = JwtAudience,
                            ["Jwt:SecretKey"]                      = JwtSecret,
                            ["Jwt:ExpirationMinutes"]              = "60",
                            ["Anthropic:Enabled"]                  = "false",
                            ["FileStorage:Provider"]               = "Local",
                            ["Mobile:RequireApiKey"]               = "false",
                        });
                    });
                });

            // Força inicialização do TestServer (migrations + seed).
            using var bootstrapClient = _factory.CreateClient();
            await bootstrapClient.GetAsync("/health");

            // Cacheia o tenant pra cada teste subsequente reusar.
            _tenant = await ResolveTenantAsync();
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient CreateClient() => _factory!.CreateClient();

    private async Task<TenantContext> ResolveTenantAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var empresa = await db.Empresas.AsNoTracking()
            .FirstAsync(e => e.Documento == CasaDocumento);
        var lojas = await db.Lojas.AsNoTracking()
            .Where(l => l.EmpresaId == empresa.Id)
            .OrderBy(l => l.Nome)
            .ToListAsync();
        lojas.Should().HaveCountGreaterThanOrEqualTo(2,
            "seed do CasaDaBaba sempre cria 2 lojas (Centro e Mercadão)");

        var token = await LoginAsync(AdminEmail, AdminSenha);
        return new TenantContext(empresa.Id, lojas[0].Id, lojas[1].Id, token);
    }

    private async Task<string> LoginAsync(string email, string senha)
    {
        using var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, senha });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();
        return token!;
    }

    private HttpClient CreateAdminClient(string token)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>
    /// Faz fluxo completo de pareamento (pair-code → pair) e devolve
    /// <c>(deviceId, apiKey)</c>. Cada chamada gera deviceId único.
    /// </summary>
    private async Task<PairedDevice> PairDeviceAsync(TenantContext tenant, Guid? lojaId = null, string? label = null)
    {
        var deviceId = "dev-test-" + Guid.NewGuid().ToString("N")[..16];

        using var admin = CreateAdminClient(tenant.Token);
        var pairCodeResp = await admin.PostAsJsonAsync("/api/mobile/devices/pair-codes", new
        {
            empresaId = tenant.EmpresaId,
            lojaId = lojaId ?? tenant.LojaId,
            deviceId,
            label
        });
        pairCodeResp.EnsureSuccessStatusCode();
        var codeBody = await pairCodeResp.Content.ReadFromJsonAsync<JsonElement>();
        var code = codeBody.GetProperty("pairingCode").GetString();

        using var anon = CreateClient();
        var pairResp = await anon.PostAsJsonAsync("/api/mobile/devices/pair", new
        {
            pairingCode = code,
            deviceId,
            label
        });
        pairResp.EnsureSuccessStatusCode();
        var pairBody = await pairResp.Content.ReadFromJsonAsync<JsonElement>();
        var apiKey = pairBody.GetProperty("apiKey").GetString();

        deviceId.Should().NotBeNullOrEmpty();
        apiKey.Should().NotBeNullOrEmpty();
        return new PairedDevice(deviceId, apiKey!);
    }

    private HttpClient CreateDeviceClient(string apiKey)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add("X-Mobile-Api-Key", apiKey);
        return c;
    }

    // ─── Smoke do /version ────────────────────────────────────────────────────

    [Fact]
    public async Task Version_endpoint_retorna_PwaCacheVersion_lido_do_sw_js_real()
    {
        if (!_isAvailable) return;

        using var client = CreateClient();
        var resp = await client.GetAsync("/api/mobile/version");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("ota").GetProperty("pwaCacheVersion").GetString()
            .Should().StartWith("cdb-")
            .And.NotBe("cdb-unknown",
                "PwaVersionProvider deve ler o sw.js servido em wwwroot/pwa, " +
                "e o sw.js real começa com 'cdb-'");

        var supportedCmds = body.GetProperty("supportedCommands").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        supportedCmds.Should().Contain(new[] { "flush_now", "pull_now", "reload", "message", "pwa_update", "clear_cache" });

        body.GetProperty("ota").GetProperty("apkVersion").GetString().Should().NotBeNull();
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    // ─── Comandos individuais ─────────────────────────────────────────────────

    [Theory]
    [InlineData("pwa_update")]
    [InlineData("clear_cache")]
    [InlineData("flush_now")]
    [InlineData("pull_now")]
    [InlineData("reload")]
    [InlineData("message")]
    public async Task EnqueueCommand_aceita_comandos_validos(string commandType)
    {
        if (!_isAvailable) return;

        var device = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType, payloadJson = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("commandType").GetString().Should().Be(commandType);
    }

    [Theory]
    [InlineData("bogus_command")]
    [InlineData("DROP TABLE")]
    [InlineData("flush")] // sem _now
    public async Task EnqueueCommand_rejeita_comandos_invalidos(string commandType)
    {
        if (!_isAvailable) return;

        var device = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType, payloadJson = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnqueueCommand_sem_auth_retorna_401()
    {
        if (!_isAvailable) return;

        var device = await PairDeviceAsync(_tenant!);

        using var anon = CreateClient();
        var resp = await anon.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType = "pwa_update", payloadJson = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnqueueCommand_em_device_revogado_retorna_BadRequest()
    {
        if (!_isAvailable) return;

        var device = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        var del = await admin.DeleteAsync($"/api/mobile/devices/{device.DeviceId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp = await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType = "pwa_update", payloadJson = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Pull commands (PWA-side) ─────────────────────────────────────────────

    [Fact]
    public async Task Device_pega_seus_comandos_via_pending_commands()
    {
        if (!_isAvailable) return;

        var device = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType = "pwa_update", payloadJson = (string?)null });
        await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{device.DeviceId}/commands",
            new { commandType = "message", payloadJson = """{"text":"oi"}""" });

        using var deviceClient = CreateDeviceClient(device.ApiKey);
        var resp = await deviceClient.GetAsync("/api/mobile/operation/pending-commands");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var arr = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        arr.Should().NotBeNull();
        arr!.Length.Should().Be(2);
        arr.Select(e => e.GetProperty("commandType").GetString())
            .Should().Contain(new[] { "pwa_update", "message" });

        // Segunda chamada não retorna nada (já entregues).
        var resp2 = await deviceClient.GetAsync("/api/mobile/operation/pending-commands");
        var arr2 = await resp2.Content.ReadFromJsonAsync<JsonElement[]>();
        arr2!.Length.Should().Be(0);
    }

    [Fact]
    public async Task Device_NAO_ve_comandos_de_outro_device()
    {
        if (!_isAvailable) return;

        var deviceA = await PairDeviceAsync(_tenant!);
        var deviceB = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        await admin.PostAsJsonAsync(
            $"/api/mobile/devices/{deviceA.DeviceId}/commands",
            new { commandType = "pwa_update", payloadJson = (string?)null });

        using var clientB = CreateDeviceClient(deviceB.ApiKey);
        var resp = await clientB.GetAsync("/api/mobile/operation/pending-commands");
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        arr!.Length.Should().Be(0,
            "comando de A não deve aparecer pra B mesmo na mesma empresa");
    }

    // ─── Broadcast ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Broadcast_pwa_update_atinge_todos_os_devices_da_empresa()
    {
        if (!_isAvailable) return;

        const int N = 5;
        var devices = new List<PairedDevice>();
        for (int i = 0; i < N; i++)
            devices.Add(await PairDeviceAsync(_tenant!));

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            commandType = "pwa_update"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // O broadcast pega TODOS os devices ativos da empresa (incluindo
        // os criados em testes anteriores). Validamos que CADA device deste
        // teste pegou seu comando — esse é o invariante crítico.
        body.GetProperty("enqueued").GetInt32().Should().BeGreaterThanOrEqualTo(N);

        foreach (var d in devices)
        {
            using var c = CreateDeviceClient(d.ApiKey);
            var pull = await c.GetAsync("/api/mobile/operation/pending-commands");
            var arr = await pull.Content.ReadFromJsonAsync<JsonElement[]>();
            arr!.Length.Should().BeGreaterThanOrEqualTo(1,
                $"device {d.DeviceId} deveria ter recebido o pwa_update do broadcast");
            arr.Select(e => e.GetProperty("commandType").GetString())
                .Should().Contain("pwa_update");
        }
    }

    [Fact]
    public async Task Broadcast_filtrado_por_lojaId_so_atinge_a_loja_certa()
    {
        if (!_isAvailable) return;

        var devLoja1 = await PairDeviceAsync(_tenant!, lojaId: _tenant!.LojaId);
        var devLoja2A = await PairDeviceAsync(_tenant!, lojaId: _tenant!.LojaId2);
        var devLoja2B = await PairDeviceAsync(_tenant!, lojaId: _tenant!.LojaId2);

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            lojaId = _tenant!.LojaId2,
            commandType = "pwa_update"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enqueued").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var deviceIds = body.GetProperty("deviceIds").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        deviceIds.Should().Contain(new[] { devLoja2A.DeviceId, devLoja2B.DeviceId });
        deviceIds.Should().NotContain(devLoja1.DeviceId,
            "device da loja1 não deve receber broadcast filtrado pra loja2");

        // Loja1 não pega o pwa_update deste broadcast.
        using var c1 = CreateDeviceClient(devLoja1.ApiKey);
        var pull1 = await c1.GetAsync("/api/mobile/operation/pending-commands");
        (await pull1.Content.ReadFromJsonAsync<JsonElement[]>())!
            .Should().BeEmpty("loja1 não foi alvo do broadcast filtrado");

        foreach (var d in new[] { devLoja2A, devLoja2B })
        {
            using var c = CreateDeviceClient(d.ApiKey);
            var pull = await c.GetAsync("/api/mobile/operation/pending-commands");
            var arr = await pull.Content.ReadFromJsonAsync<JsonElement[]>();
            arr!.Should().Contain(e => e.GetProperty("commandType").GetString() == "pwa_update");
        }
    }

    [Fact]
    public async Task Broadcast_ignora_devices_revogados()
    {
        if (!_isAvailable) return;

        var d1 = await PairDeviceAsync(_tenant!);
        var d2 = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        await admin.DeleteAsync($"/api/mobile/devices/{d2.DeviceId}");

        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            commandType = "pwa_update"
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var deviceIds = body.GetProperty("deviceIds").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        deviceIds.Should().Contain(d1.DeviceId);
        deviceIds.Should().NotContain(d2.DeviceId, "device revogado deve ser excluído do broadcast");
    }

    [Fact]
    public async Task Broadcast_ignora_devices_pendentes_de_pareamento()
    {
        if (!_isAvailable) return;

        var paired = await PairDeviceAsync(_tenant!);

        using var admin = CreateAdminClient(_tenant!.Token);
        // Cria pair-code mas NÃO completa (fica pending com pairing_code != null).
        var pendResp = await admin.PostAsJsonAsync("/api/mobile/devices/pair-codes", new
        {
            empresaId = _tenant!.EmpresaId,
            lojaId = _tenant!.LojaId,
            deviceId = (string?)null,
            label = "device-pendente"
        });
        pendResp.EnsureSuccessStatusCode();
        var pendBody = await pendResp.Content.ReadFromJsonAsync<JsonElement>();
        var pendingId = pendBody.GetProperty("deviceRecordId").GetString();

        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            commandType = "pwa_update"
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var deviceIds = body.GetProperty("deviceIds").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        deviceIds.Should().Contain(paired.DeviceId);
        deviceIds.Should().NotContain(pendingId,
            "device com pairing_code != null não deve ser alvo de broadcast");
    }

    [Fact]
    public async Task Broadcast_cross_tenant_retorna_Forbidden()
    {
        if (!_isAvailable) return;

        // Admin do CasaDaBaba tentando broadcast para uma empresa qualquer
        // (que não a dele). RequestedEmpresaMatchesCurrentUser deve barrar.
        var empresaInimiga = Guid.NewGuid();

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = empresaInimiga,
            commandType = "pwa_update"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("bogus_op")]
    [InlineData("")]
    public async Task Broadcast_rejeita_commandType_invalido(string commandType)
    {
        if (!_isAvailable) return;

        using var admin = CreateAdminClient(_tenant!.Token);
        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            commandType
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Stress tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task STRESS_50_devices_recebem_pwa_update_em_um_broadcast()
    {
        if (!_isAvailable) return;

        const int N = 50;

        // Pareamento sequencial (paralelo congestiona TestServer + facilita debug)
        var devices = new List<PairedDevice>();
        for (int i = 0; i < N; i++)
            devices.Add(await PairDeviceAsync(_tenant!));

        using var admin = CreateAdminClient(_tenant!.Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
        {
            empresaId = _tenant!.EmpresaId,
            commandType = "pwa_update"
        });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enqueued").GetInt32().Should().BeGreaterThanOrEqualTo(N);

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "broadcast pra 50 devices é INSERT em batch — não deve passar de 15s mesmo em CI lento");

        // Pull em paralelo: cada device deste teste deve pegar pelo menos 1 comando.
        var pullTasks = devices.Select(async d =>
        {
            using var c = CreateDeviceClient(d.ApiKey);
            var pull = await c.GetAsync("/api/mobile/operation/pending-commands");
            var arr = await pull.Content.ReadFromJsonAsync<JsonElement[]>();
            return (d.DeviceId, count: arr!.Length, hasUpdate: arr.Any(e => e.GetProperty("commandType").GetString() == "pwa_update"));
        });
        var pulls = await Task.WhenAll(pullTasks);
        pulls.Should().AllSatisfy(p =>
        {
            p.hasUpdate.Should().BeTrue(
                $"device {p.DeviceId} deveria ter recebido pwa_update no broadcast");
        });
    }

    [Fact]
    public async Task CONCURRENCY_10_broadcasts_paralelos_sao_atomicos_por_device()
    {
        if (!_isAvailable) return;

        const int Devices = 5;
        const int Broadcasts = 10;

        var devices = new List<PairedDevice>();
        for (int i = 0; i < Devices; i++)
            devices.Add(await PairDeviceAsync(_tenant!));

        var tipos = new[] { "pwa_update", "clear_cache", "reload", "flush_now", "pull_now" };
        var tasks = Enumerable.Range(0, Broadcasts).Select(async i =>
        {
            using var admin = CreateAdminClient(_tenant!.Token);
            var resp = await admin.PostAsJsonAsync("/api/mobile/devices/broadcast", new
            {
                empresaId = _tenant!.EmpresaId,
                commandType = tipos[i % tipos.Length]
            });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        });
        await Task.WhenAll(tasks);

        // Cada device deve ter recebido EXATAMENTE 10 comandos novos
        // (o restante eram zero antes do broadcast pq são devices novos).
        // Se tivesse race condition o número viria errado.
        foreach (var d in devices)
        {
            using var c = CreateDeviceClient(d.ApiKey);
            var pull = await c.GetAsync("/api/mobile/operation/pending-commands");
            var arr = await pull.Content.ReadFromJsonAsync<JsonElement[]>();
            arr!.Length.Should().Be(Broadcasts,
                $"device {d.DeviceId} deveria ter exatamente {Broadcasts} comandos " +
                "(1 por broadcast, sem race condition que duplique ou perca)");
        }
    }

    // ─── Cache do PwaVersionProvider ──────────────────────────────────────────

    [Fact]
    public async Task PwaCacheVersion_serve_do_cache_dentro_do_TTL_quando_sw_js_muda()
    {
        if (!_isAvailable) return;

        using var client = CreateClient();
        var resp1 = await client.GetAsync("/api/mobile/version");
        var v1 = (await resp1.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ota").GetProperty("pwaCacheVersion").GetString();
        v1.Should().StartWith("cdb-");

        // Modifica o sw.js servido pelo TestServer (caminho real).
        var env = _factory!.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var swPath = Path.Combine(env.WebRootPath, "pwa", "sw.js");
        if (!File.Exists(swPath)) return; // ambiente sem wwwroot — skip

        var original = await File.ReadAllTextAsync(swPath);
        try
        {
            var novo = "cdb-stress-" + Guid.NewGuid().ToString("N")[..8];
            var modified = System.Text.RegularExpressions.Regex.Replace(
                original,
                @"const\s+CACHE_VERSION\s*=\s*['""][^'""]+['""]",
                $"const CACHE_VERSION = '{novo}'");
            await File.WriteAllTextAsync(swPath, modified);

            // Cache TTL = 60s. Dentro desse intervalo, /version mantém o valor antigo.
            var resp2 = await client.GetAsync("/api/mobile/version");
            var v2 = (await resp2.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("ota").GetProperty("pwaCacheVersion").GetString();
            v2.Should().Be(v1, "dentro do TTL, /version não deve relê o disco");
            v2.Should().NotBe(novo);
        }
        finally
        {
            await File.WriteAllTextAsync(swPath, original);
        }
    }

    // ─── Tipos auxiliares ─────────────────────────────────────────────────────

    private sealed record TenantContext(
        Guid EmpresaId,
        Guid LojaId,
        Guid LojaId2,
        string Token);

    private sealed record PairedDevice(string DeviceId, string ApiKey);
}
