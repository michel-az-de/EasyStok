using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public class ApkDistributionE2ETests(MobileE2EFixture fixture)
{
    [Fact]
    public async Task Manifest_sem_release_retorna_204()
    {
        if (!fixture.IsAvailable) return;
        await CleanReleasesAsync("manifest-empty-app");

        using var client = fixture.CreateClient();
        var resp = await client.GetAsync("/api/mobile/apk/manifest?appId=manifest-empty-app");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Manifest_retorna_release_active_com_sha_e_url()
    {
        if (!fixture.IsAvailable) return;
        const string appId = "test-stable";
        await CleanReleasesAsync(appId);

        var apk = MakeFakeApk(1024);
        var sha = Convert.ToHexString(SHA256.HashData(apk)).ToLowerInvariant();
        var releaseId = await SeedReleaseAsync(appId, "1.0.0", apk, sha, isCanaryOnly: false);

        using var client = fixture.CreateClient();
        var resp = await client.GetAsync($"/api/mobile/apk/manifest?appId={appId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetString().Should().Be("1.0.0");
        body.GetProperty("sha256").GetString().Should().Be(sha);
        body.GetProperty("isCanary").GetBoolean().Should().BeFalse();
        body.GetProperty("url").GetString().Should().Contain($"/api/mobile/apk/download/{releaseId}");
    }

    [Fact]
    public async Task Download_retorna_bytes_corretos_para_release_active()
    {
        if (!fixture.IsAvailable) return;
        const string appId = "test-download";
        await CleanReleasesAsync(appId);

        var apk = MakeFakeApk(2048);
        var sha = Convert.ToHexString(SHA256.HashData(apk)).ToLowerInvariant();
        var releaseId = await SeedReleaseAsync(appId, "2.0.0", apk, sha, isCanaryOnly: false);

        using var client = fixture.CreateClient();
        var resp = await client.GetAsync($"/api/mobile/apk/download/{releaseId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.android.package-archive");
        var downloaded = await resp.Content.ReadAsByteArrayAsync();
        downloaded.Should().BeEquivalentTo(apk);
        var downloadedSha = Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant();
        downloadedSha.Should().Be(sha);
    }

    [Fact]
    public async Task Manifest_para_device_canary_retorna_release_canary_quando_disponivel()
    {
        if (!fixture.IsAvailable) return;
        const string appId = "test-canary";
        await CleanReleasesAsync(appId);

        var stableApk = MakeFakeApk(1024);
        await SeedReleaseAsync(appId, "1.0.0", stableApk,
            Convert.ToHexString(SHA256.HashData(stableApk)).ToLowerInvariant(),
            isCanaryOnly: false);

        var canaryApk = MakeFakeApk(2048);
        await SeedReleaseAsync(appId, "1.1.0-canary", canaryApk,
            Convert.ToHexString(SHA256.HashData(canaryApk)).ToLowerInvariant(),
            isCanaryOnly: true);

        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var creds = await fixture.SeedMobileDeviceAsync(empresaId, lojaId, isCanary: true);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", creds.DeviceId);
        var resp = await client.GetAsync($"/api/mobile/apk/manifest?appId={appId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetString().Should().Be("1.1.0-canary");
        body.GetProperty("isCanary").GetBoolean().Should().BeTrue();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static byte[] MakeFakeApk(int size)
    {
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[size];
        rng.GetBytes(bytes);
        return bytes;
    }

    private async Task<Guid> SeedReleaseAsync(string appId, string version,
        byte[] content, string sha, bool isCanaryOnly)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var release = new ApkRelease
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Version = version,
            Sha256 = sha,
            FileContent = content,
            FileSizeBytes = content.LongLength,
            IsCanaryOnly = isCanaryOnly,
            IsActive = true,
            CriadoEm = DateTime.UtcNow,
        };
        db.ApkReleases.Add(release);
        await db.SaveChangesAsync();
        return release.Id;
    }

    private async Task CleanReleasesAsync(string appId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.ApkReleases.Where(r => r.AppId == appId)
            .ExecuteDeleteAsync();
    }
}
