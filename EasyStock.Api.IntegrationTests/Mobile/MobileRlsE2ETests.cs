using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2180 de ponta a ponta: a API roda com login sem superusuário nem BYPASSRLS e as tabelas
/// <c>mobile_*</c> têm <c>tenant_isolation</c>. O aparelho autentica (lookup antes do tenant), envia e
/// puxa pelo sync, e o aparelho de outra empresa não recebe nada dele.
/// </summary>
[Collection("MobileE2E")]
public class MobileRlsE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Api_roda_sujeita_a_RLS()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var privilegiado = await db.Database.SqlQueryRaw<bool>(
            "SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync();
        privilegiado.Should().BeFalse("sem isso a policy não vale e o E2E não prova o APK sob RLS");
    }

    [SkippableFact]
    public async Task Sync_do_aparelho_funciona_e_nao_cruza_empresas()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        var a = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var outroDeA = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var b = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var produto = $"p-{Guid.NewGuid():N}"[..24];

        var push = await fixture.CreateMobileClient(a.ApiKey).PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = a.DeviceId,
            operatorName = "Thati",
            mutations = new[]
            {
                new
                {
                    id = "mut_" + Guid.NewGuid(), deviceId = a.DeviceId, type = "product.upsert",
                    payload = JsonSerializer.SerializeToElement(
                        new { id = produto, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = 4, custom = true }, Json),
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
            },
        }, Json);
        push.StatusCode.Should().Be(HttpStatusCode.OK, await push.Content.ReadAsStringAsync());
        var corpo = await push.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("acceptedIds").GetArrayLength().Should().Be(1, corpo.ToString());

        // O pull não devolve ao aparelho o que ele mesmo enviou; o outro aparelho da empresa recebe.
        (await PullIdsAsync(outroDeA)).Should().Contain(produto);
        (await PullIdsAsync(b)).Should().NotContain(produto, "o aparelho da outra empresa não enxerga a linha");
    }

    [SkippableFact]
    public async Task Api_key_desconhecida_continua_401()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var resp = await fixture.CreateMobileClient("tk-inexistente").GetAsync("/api/mobile/sync/pull?since=0&deviceId=x");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<List<string>> PullIdsAsync(MobileDeviceCredentials device)
    {
        var resp = await fixture.CreateMobileClient(device.ApiKey)
            .GetAsync($"/api/mobile/sync/pull?since=0&deviceId={device.DeviceId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return corpo.GetProperty("mutations").EnumerateArray()
            .Select(m => m.GetProperty("payload").GetProperty("id").GetString()!)
            .ToList();
    }
}
