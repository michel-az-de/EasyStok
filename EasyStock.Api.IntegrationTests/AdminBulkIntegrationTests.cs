using DotNet.Testcontainers.Builders;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Integração das ações em massa de clientes (bulk/status, bulk/plano). Sobe a API
/// real contra um Postgres efêmero (Testcontainers), cria tenants via API e exercita
/// falha parcial, jaNoEstado e troca de plano. Sem Docker, os testes são PULADOS.
/// </summary>
public sealed class AdminBulkIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;
    private string? _connString;

    public async Task InitializeAsync()
    {
        var externalPg = Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG");
        if (!string.IsNullOrWhiteSpace(externalPg))
        {
            _connString = externalPg;
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", externalPg);
            Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
            Environment.SetEnvironmentVariable("RunMigrationsOnStartup", "true");
            Environment.SetEnvironmentVariable("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001");
            Environment.SetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD", "Integr4cao-T3st-SuperAdmin");
            _isAvailable = true;
            return;
        }

        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_bulk_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
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

    private WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:DefaultConnection"] = _connString,
                ["RunMigrationsOnStartup"] = "true",
                ["Mobile:ApiKey"] = "easystock-integration-test-mobile-key-0001",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Jwt:Issuer"] = "EasyStock",
                ["Jwt:Audience"] = "EasyStock",
                ["Jwt:SecretKey"] = "EasyStock-Test-SuperSecretKey-Min32Chars!!",
                ["Jwt:ExpirationMinutes"] = "60",
                ["Anthropic:Enabled"] = "false",
                ["FileStorage:Provider"] = "Local"
            })));

    private static async Task<HttpClient?> ClienteAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "felipe@easystock.com", Senha = "Admin@2026!Secure" });
        if (!login.IsSuccessStatusCode) return null; // seed indisponível → caller pula
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    private static JsonElement Data(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("data", out var d) ? d : body;

    private static async Task<Guid> CriarTenantAsync(HttpClient client, string nome)
    {
        var email = $"bulk-{Guid.NewGuid():N}@teste.easystock.com";
        var resp = await client.PostAsJsonAsync("/api/admin/tenants", new
        {
            motivo = "Tenant de teste de acao em massa",
            nomeEmpresa = nome,
            nomeAdmin = "Admin Teste",
            emailAdmin = email,
            enviarEmail = false
        });
        resp.IsSuccessStatusCode.Should().BeTrue($"criar tenant '{nome}' deveria funcionar; corpo: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Data(body).GetProperty("tenantId").GetGuid();
    }

    private static async Task<Guid> CriarTicketAsync(HttpClient client, Guid empresaId, string titulo)
    {
        var resp = await client.PostAsJsonAsync("/api/admin/tickets", new
        {
            empresaId,
            titulo,
            descricao = "Descricao de teste do ticket em lote",
            categoria = "Duvida",
            prioridade = "Normal",
            nivel = "N1"
        });
        resp.IsSuccessStatusCode.Should().BeTrue($"criar ticket deveria funcionar; corpo: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Data(body).GetProperty("id").GetGuid();
    }

    private static async Task<string?> StatusDoTenantAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/admin/tenants/{id}");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var assinatura = Data(body).GetProperty("assinatura");
        return assinatura.ValueKind == JsonValueKind.Null ? null : assinatura.GetProperty("status").GetString();
    }

    [SkippableFact]
    public async Task BulkStatus_suspende_em_lote_com_falha_parcial_e_idempotencia()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        await using var factory = CriarFactory();
        var client = await ClienteAdminAsync(factory);
        Skip.If(client is null, "Seed admin indisponível neste ambiente.");

        var id1 = await CriarTenantAsync(client!, "Bulk Cliente A");
        var id2 = await CriarTenantAsync(client!, "Bulk Cliente B");
        var fake = Guid.NewGuid();

        // Suspende [id1, id2, fake] — 2 sucesso, 1 falha.
        var resp = await client!.PostAsJsonAsync("/api/admin/tenants/bulk/status",
            new { ids = new[] { id1, id2, fake }, status = "Suspensa", motivo = "Suspensao em massa de teste" });
        resp.IsSuccessStatusCode.Should().BeTrue();
        var data = Data(await resp.Content.ReadFromJsonAsync<JsonElement>());

        data.GetProperty("total").GetInt32().Should().Be(3);
        data.GetProperty("sucesso").GetInt32().Should().Be(2);
        data.GetProperty("falhas").GetArrayLength().Should().Be(1);
        data.GetProperty("falhas")[0].GetProperty("id").GetGuid().Should().Be(fake);

        // Persistiu: id1 ficou Suspensa.
        (await StatusDoTenantAsync(client!, id1)).Should().Be("Suspensa");

        // Trilha 1 (AuditLog de dominio) gravada para a acao em lote.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var houveAudit = await db.AuditLogs.AsNoTracking()
                .AnyAsync(a => a.Acao == "AdminAlterarStatusTenantLote:Suspensa");
            houveAudit.Should().BeTrue();
        }

        // Idempotencia: re-suspender id1 (ja Suspensa) -> jaNoEstado, sem novo sucesso.
        var resp2 = await client!.PostAsJsonAsync("/api/admin/tenants/bulk/status",
            new { ids = new[] { id1 }, status = "Suspensa", motivo = "Re-suspensao idempotente" });
        var data2 = Data(await resp2.Content.ReadFromJsonAsync<JsonElement>());
        data2.GetProperty("sucesso").GetInt32().Should().Be(0);
        data2.GetProperty("jaNoEstado").GetInt32().Should().Be(1);
    }

    [SkippableFact]
    public async Task BulkPlano_troca_plano_em_lote()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        await using var factory = CriarFactory();
        var client = await ClienteAdminAsync(factory);
        Skip.If(client is null, "Seed admin indisponível neste ambiente.");

        var id1 = await CriarTenantAsync(client!, "Bulk Plano A");
        var id2 = await CriarTenantAsync(client!, "Bulk Plano B");

        // Pega um plano disponivel.
        var planosResp = await client!.GetAsync("/api/admin/planos");
        planosResp.IsSuccessStatusCode.Should().BeTrue();
        var planosBody = Data(await planosResp.Content.ReadFromJsonAsync<JsonElement>());
        Skip.If(planosBody.ValueKind != JsonValueKind.Array || planosBody.GetArrayLength() == 0,
            "Nenhum plano disponivel no seed.");
        var planoAlvo = planosBody[planosBody.GetArrayLength() - 1].GetProperty("id").GetGuid();

        var resp = await client!.PostAsJsonAsync("/api/admin/tenants/bulk/plano",
            new { ids = new[] { id1, id2 }, planoId = planoAlvo });
        resp.IsSuccessStatusCode.Should().BeTrue();
        var data = Data(await resp.Content.ReadFromJsonAsync<JsonElement>());

        data.GetProperty("sucesso").GetInt32().Should().Be(2);
        data.GetProperty("falhas").GetArrayLength().Should().Be(0);

        // Persistiu: o plano da assinatura de id1 passou a ser o alvo.
        var detalhe = await client!.GetAsync($"/api/admin/tenants/{id1}");
        var assinatura = Data(await detalhe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assinatura");
        assinatura.GetProperty("plano").GetProperty("id").GetGuid().Should().Be(planoAlvo);
    }

    [SkippableFact]
    public async Task BulkTickets_fechar_com_falha_parcial_e_assumir_em_lote()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível — Postgres de teste não pôde subir.");
        await using var factory = CriarFactory();
        var client = await ClienteAdminAsync(factory);
        Skip.If(client is null, "Seed admin indisponível neste ambiente.");

        var empresaId = await CriarTenantAsync(client!, "Bulk Tickets Co");
        var t1 = await CriarTicketAsync(client!, empresaId, "Ticket lote 1");
        var t2 = await CriarTicketAsync(client!, empresaId, "Ticket lote 2");
        var t3 = await CriarTicketAsync(client!, empresaId, "Ticket lote 3");
        var fake = Guid.NewGuid();

        // Fechar [t1, t2, fake] -> 2 sucesso, 1 falha.
        var respFechar = await client!.PostAsJsonAsync("/api/admin/tickets/bulk/status",
            new { ids = new[] { t1, t2, fake }, status = "Fechado" });
        respFechar.IsSuccessStatusCode.Should().BeTrue();
        var dataFechar = Data(await respFechar.Content.ReadFromJsonAsync<JsonElement>());
        dataFechar.GetProperty("sucesso").GetInt32().Should().Be(2);
        dataFechar.GetProperty("falhas").GetArrayLength().Should().Be(1);
        dataFechar.GetProperty("falhas")[0].GetProperty("id").GetGuid().Should().Be(fake);

        // Assumir [t3] -> 1 sucesso, atribui o ticket ao operador atual.
        var respAssumir = await client!.PostAsJsonAsync("/api/admin/tickets/bulk/assumir",
            new { ids = new[] { t3 } });
        respAssumir.IsSuccessStatusCode.Should().BeTrue();
        var dataAssumir = Data(await respAssumir.Content.ReadFromJsonAsync<JsonElement>());
        dataAssumir.GetProperty("sucesso").GetInt32().Should().Be(1);

        // Persistencia via DbContext: t1 fechado, t3 com atendente atribuido.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var t1Status = await db.AdminTickets.AsNoTracking().Where(t => t.Id == t1).Select(t => t.Status).FirstAsync();
        t1Status.Should().Be(EasyStock.Domain.Enums.TicketStatus.Fechado);
        var t3Atendente = await db.AdminTickets.AsNoTracking().Where(t => t.Id == t3).Select(t => t.AtendenteId).FirstAsync();
        t3Atendente.Should().NotBeNull();
    }
}
