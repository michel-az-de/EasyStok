using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public sealed class PushReceiptsE2ETests(MobileE2EFixture fixture)
{
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string Id(ECDsa key) => Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    private static PushEnvelope Signed(ECDsa key, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return new(Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }
    private static async Task Register(HttpClient http, ECDsa key)
    {
        var response = await http.PostAsJsonAsync("/api/mobile/push/installations", Signed(key, new
        { kind = "cdb-installation-v1", installationId = Id(key), token = "fcm-test-" + Guid.NewGuid(), apkVersion = "0.6.0", issuedAt = Now }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
    private async Task<PushMessage> SeedNotice(string target)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        // O despacho RSA é coberto nos testes do controlador. Aqui exercitamos HTTP, SQL e recibos do aparelho.
        var notice = new PushMessage { Id = Guid.NewGuid(), InstallationId = target, Nonce = Guid.NewGuid().ToString("N"),
            CreatedAt = Now, ExpiresAt = Now + 600000, PayloadBase64 = "e30=", SignatureBase64 = "dGVzdA==" };
        db.Add(notice); await db.SaveChangesAsync(); return notice;
    }
    private static Task<HttpResponseMessage> Receipt(HttpClient http, ECDsa key, PushMessage notice, string source = "push", string evt = "received", string? nonce = null) =>
        http.PostAsJsonAsync("/api/mobile/push/receipts", Signed(key, new
        {
            kind = "cdb-receipt-v1", installationId = Id(key), messageId = notice.Id, nonce = nonce ?? notice.Nonce,
            @event = evt, source, occurredAt = Now - 86400000L, apkVersion = "0.6.0"
        }));

    [SkippableFact]
    public async Task HttpPostgres_RegistroSemPareamentoERecibosPersistentesIdempotentes()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        using var http = fixture.CreateClient(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Register(http, key); var notice = await SeedNotice(Id(key));
        var inbox = await http.PostAsJsonAsync("/api/mobile/push/inbox", Signed(key, new { kind = "cdb-inbox-v1", installationId = Id(key), issuedAt = Now }));
        inbox.StatusCode.Should().Be(HttpStatusCode.OK, await inbox.Content.ReadAsStringAsync());
        (await inbox.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("messages").GetArrayLength().Should().Be(1);
        (await Receipt(http, key, notice, "sync")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Receipt(http, key, notice)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await Receipt(http, key, notice);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("received").GetBoolean().Should().BeTrue();
        (await Receipt(http, key, notice, evt: "opened")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var receipts = await db.Set<PushReceipt>().Where(x => x.MessageId == notice.Id).ToListAsync();
        receipts.Should().HaveCount(3);
        receipts.Should().OnlyContain(x => x.ConfirmedAt > x.OccurredAt);
        receipts.Should().ContainSingle(x => x.Event == "received" && x.Source == "push");
        (await db.Set<MobileDevice>().AnyAsync(x => x.Id == Id(key))).Should().BeFalse();
    }

    [SkippableFact]
    public async Task HttpPostgres_OutraInstalacaoNaoConsultaNemConfirmaAvisoAlheio()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        using var http = fixture.CreateClient(); using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Register(http, owner); await Register(http, other); var notice = await SeedNotice(Id(owner));
        var inbox = await http.PostAsJsonAsync("/api/mobile/push/inbox", Signed(other, new { kind = "cdb-inbox-v1", installationId = Id(other), issuedAt = Now }));
        inbox.StatusCode.Should().Be(HttpStatusCode.OK);
        (await inbox.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("messages").GetArrayLength().Should().Be(0);
        (await Receipt(http, other, notice)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Receipt(http, owner, notice, nonce: "nonce-errado")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var forgery = await http.PostAsJsonAsync("/api/mobile/push/inbox", Signed(other, new { kind = "cdb-inbox-v1", installationId = Id(owner), issuedAt = Now }));
        forgery.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Postgres_ConcorrenciaNaoDeixaRegistroAntigoSobrescreverTokenNovo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        using var http = fixture.CreateClient(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await Register(http, key);
        using var scopeNew = fixture.Factory!.Services.CreateScope(); using var scopeOld = fixture.Factory.Services.CreateScope();
        var newerDb = scopeNew.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var olderDb = scopeOld.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var id = Id(key);
        var newer = await newerDb.Set<PushInstallation>().SingleAsync(x => x.Id == id);
        var older = await olderDb.Set<PushInstallation>().SingleAsync(x => x.Id == id);
        newer.IssuedAt += 2; newer.Token = "token-mais-novo";
        older.IssuedAt += 1; older.Token = "token-antigo";
        await newerDb.SaveChangesAsync();
        Func<Task> staleWrite = () => olderDb.SaveChangesAsync();
        await staleWrite.Should().ThrowAsync<DbUpdateConcurrencyException>();
        newerDb.ChangeTracker.Clear();
        (await newerDb.Set<PushInstallation>().SingleAsync(x => x.Id == id)).Token.Should().Be("token-mais-novo");
    }
}
