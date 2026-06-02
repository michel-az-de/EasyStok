using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public class OtaCanaryE2ETests(MobileE2EFixture fixture)
{
    [SkippableFact]
    public async Task Version_sem_device_id_retorna_stable()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");

        using var client = fixture.CreateClient();
        var resp = await client.GetAsync("/api/mobile/version");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ota = body.GetProperty("ota");
        ota.GetProperty("pwaCacheVersion").GetString().Should().Be("cdb-v3-stable-test");
        ota.GetProperty("isCanaryDevice").GetBoolean().Should().BeFalse();
        ota.GetProperty("otaEnabled").GetBoolean().Should().BeTrue();
    }

    [SkippableFact]
    public async Task Version_com_device_canary_retorna_versao_canary_do_sw()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");

        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var creds = await fixture.SeedMobileDeviceAsync(empresaId, lojaId, isCanary: true);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", creds.DeviceId);
        var resp = await client.GetAsync("/api/mobile/version");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ota = body.GetProperty("ota");

        // Stable e canary expostos em campos separados — UI pode mostrar diff
        var stable = ota.GetProperty("pwaCacheVersionStable").GetString();
        var canary = ota.GetProperty("pwaCacheVersionCanary").GetString();
        ota.GetProperty("isCanaryDevice").GetBoolean().Should().BeTrue();

        // pwaCacheVersion resolve pra canary quando device.IsCanary=true
        ota.GetProperty("pwaCacheVersion").GetString().Should().Be(canary);
        stable.Should().Be("cdb-v3-stable-test");
        canary.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Version_com_device_nao_canary_retorna_stable_mesmo_que_canary_disponivel()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");

        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var creds = await fixture.SeedMobileDeviceAsync(empresaId, lojaId, isCanary: false);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", creds.DeviceId);
        var resp = await client.GetAsync("/api/mobile/version");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ota = body.GetProperty("ota");
        ota.GetProperty("pwaCacheVersion").GetString().Should().Be("cdb-v3-stable-test");
        ota.GetProperty("isCanaryDevice").GetBoolean().Should().BeFalse();
    }
}
