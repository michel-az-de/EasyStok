using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;

namespace EasyStock.Api.UnitTests.Mobile;

public sealed class PushReceiptsTests : IDisposable
{
    private readonly ECDsa device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly RSA publisher = RSA.Create(3072);
    private readonly EasyStockDbContext db = new(new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private string DeviceId => Convert.ToHexStringLower(SHA256.HashData(device.ExportSubjectPublicKeyInfo()));
    private sealed class Controller(EasyStockDbContext db, byte[] key) : PushReceiptsController(db)
    { protected override byte[] PublisherKey => key; }
    private Controller Api => new(db, publisher.ExportSubjectPublicKeyInfo());
    private PushEnvelope SignDevice(object value, ECDsa? key = null)
    {
        key ??= device;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return new(Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }
    private PushEnvelope SignPublisher(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return new(Convert.ToBase64String(bytes), Convert.ToBase64String(publisher.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }
    private Task<IActionResult> Register(long? issued = null, string token = "token-fcm-teste") => Api.Register(SignDevice(new
    { kind = "cdb-installation-v1", installationId = DeviceId, token, apkVersion = "0.6.0", issuedAt = issued ?? Now }), default);
    private PushEnvelope Notice(Guid id, string? target = null, long? created = null) => SignPublisher(new
    {
        kind = "cdb-notice-v1", id, createdAt = created ?? Now, expiresAt = Now + 86400000L, target = target ?? DeviceId,
        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        title = "Aviso público de teste", body = "Sem dados operacionais.", screen = "home"
    });
    private async Task<Guid> Dispatch()
    {
        (await Register()).Should().BeOfType<OkObjectResult>();
        var id = Guid.NewGuid(); (await Api.Dispatch(Notice(id), default)).Should().BeOfType<OkObjectResult>(); return id;
    }
    private Task<IActionResult> Receipt(Guid id, string evt = "received", string source = "push", string? nonce = null, ECDsa? signer = null) => Api.Receipt(SignDevice(new
    {
        kind = "cdb-receipt-v1", installationId = signer == null ? DeviceId : Convert.ToHexStringLower(SHA256.HashData(signer.ExportSubjectPublicKeyInfo())),
        messageId = id, nonce = nonce ?? db.Set<PushMessage>().Single(x => x.Id == id).Nonce,
        @event = evt, source, occurredAt = Now - 3 * 86400000L, apkVersion = "0.6.0"
    }, signer), default);
    private Task<IActionResult> Provider(Guid id, string status, Guid? request = null) => Api.Control(SignPublisher(new
    { kind = "cdb-push-control-v1", action = "provider", issuedAt = Now, requestId = request ?? Guid.NewGuid(), messageId = id,
        installationId = DeviceId, status, providerMessageId = status == "accepted" ? "projects/test/messages/123" : null }), default);
    private async Task<JsonElement> Report(Guid id) => JsonSerializer.SerializeToElement(((OkObjectResult)await Api.Control(SignPublisher(new
    { kind = "cdb-push-control-v1", action = "report", issuedAt = Now, requestId = Guid.NewGuid(), messageId = id }), default)).Value);

    [Fact]
    public async Task RegistroAutenticadoNaoExigeEmpresaENaoExpoeTokenNaListagem()
    {
        (await Register()).Should().BeOfType<OkObjectResult>();
        var result = (OkObjectResult)await Api.Control(SignPublisher(new { kind = "cdb-push-control-v1", action = "installations", issuedAt = Now, requestId = Guid.NewGuid() }), default);
        var json = JsonSerializer.SerializeToElement(result.Value);
        json.GetProperty("installations")[0].GetProperty("installationId").GetString().Should().Be(DeviceId);
        json.GetRawText().Should().NotContain("token-fcm-teste");
        db.Set<MobileDevice>().Should().BeEmpty();
    }
    [Fact]
    public async Task RegistroMonotonicoRejeitaReplayAntigoEConflitoNoMesmoInstante()
    {
        var now = Now; (await Register(now)).Should().BeOfType<OkObjectResult>();
        (await Register(now)).Should().BeOfType<OkObjectResult>();
        (await Register(now - 1, "outro-token")).Should().BeOfType<ConflictObjectResult>();
        (await Register(now, "outro-token")).Should().BeOfType<ConflictObjectResult>();
        (await Register(now + 1, "novo-token")).Should().BeOfType<OkObjectResult>();
        db.Set<PushInstallation>().Single().Token.Should().Be("novo-token");
    }
    [Fact]
    public async Task PayloadAlteradoOuChaveDiferenteNaoAutenticaInstalacao()
    {
        var envelope = SignDevice(new { kind = "cdb-installation-v1", installationId = DeviceId, token = "test", apkVersion = "0.6.0", issuedAt = Now });
        envelope = envelope with { PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64)).Replace("0.6.0", "0.7.0"))) };
        (await Api.Register(envelope, default)).Should().BeOfType<UnauthorizedObjectResult>();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        (await Api.Register(SignDevice(new { kind = "cdb-installation-v1", installationId = DeviceId, token = "test", apkVersion = "0.6.0", issuedAt = Now }, other), default)).Should().BeOfType<UnauthorizedObjectResult>();
        db.Set<PushInstallation>().Should().BeEmpty();
    }
    [Fact]
    public async Task PublicadorNaoPodeSerSubstituidoPelaChaveDoAparelho()
    {
        (await Api.Control(SignDevice(new { kind = "cdb-push-control-v1", action = "installations", issuedAt = Now, requestId = Guid.NewGuid() }), default)).Should().BeOfType<BadRequestObjectResult>();
        (await Api.Control(SignPublisher(new { kind = "cdb-push-control-v1", action = "installations", issuedAt = Now - 600000, requestId = Guid.NewGuid() }), default)).Should().BeOfType<UnauthorizedObjectResult>();
    }
    [Fact]
    public async Task DespachoIdempotenteNaoPermiteTrocarAvisoNemReutilizarNonce()
    {
        await Register(); var id = Guid.NewGuid(); var notice = Notice(id);
        (await Api.Dispatch(notice, default)).Should().BeOfType<OkObjectResult>();
        (await Api.Dispatch(notice, default)).Should().BeOfType<OkObjectResult>();
        (await Api.Dispatch(Notice(id), default)).Should().BeOfType<ConflictObjectResult>();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(Convert.FromBase64String(notice.PayloadBase64))!;
        payload["id"] = Guid.NewGuid();
        (await Api.Dispatch(SignPublisher(payload), default)).Should().BeOfType<ConflictObjectResult>();
        db.Set<PushMessage>().Count().Should().Be(1);
    }
    [Fact]
    public async Task AceiteDoFirebaseNaoSignificaRecebimentoEFalhaNaoApagaAceite()
    {
        var id = await Dispatch(); await Provider(id, "accepted"); await Provider(id, "failed");
        var report = await Report(id);
        report.GetProperty("providerStatus").GetString().Should().Be("accepted");
        report.GetProperty("receivedPushAt").ValueKind.Should().Be(JsonValueKind.Null);
        report.GetProperty("receivedSyncAt").ValueKind.Should().Be(JsonValueKind.Null);
        report.GetProperty("openedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
    [Fact]
    public async Task RecibosOfflineIdempotentesSepararamPushDeSincronizacaoEAbertura()
    {
        var id = await Dispatch();
        (await Receipt(id, source: "sync")).Should().BeOfType<OkObjectResult>();
        var onlySync = await Report(id); onlySync.GetProperty("receivedPushAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await Receipt(id)).Should().BeOfType<OkObjectResult>();
        var original = db.Set<PushReceipt>().Single(x => x.Source == "push").ConfirmedAt;
        (await Receipt(id)).Should().BeOfType<OkObjectResult>();
        (await Receipt(id, "opened")).Should().BeOfType<OkObjectResult>();
        db.Set<PushReceipt>().Count().Should().Be(3);
        db.Set<PushReceipt>().Single(x => x.Source == "push" && x.Event == "received").ConfirmedAt.Should().Be(original);
        var report = await Report(id); report.GetProperty("openedAt").GetInt64().Should().BeGreaterThan(0);
    }
    [Fact]
    public async Task NonceIncorretoEInstalacaoAlheiaNaoAssinamEntrega()
    {
        var id = await Dispatch();
        (await Receipt(id, nonce: "nonce-incorreto")).Should().BeOfType<UnauthorizedObjectResult>();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherId = Convert.ToHexStringLower(SHA256.HashData(other.ExportSubjectPublicKeyInfo()));
        await Api.Register(SignDevice(new { kind = "cdb-installation-v1", installationId = otherId, token = "outro", apkVersion = "0.6.0", issuedAt = Now }, other), default);
        (await Receipt(id, signer: other)).Should().BeOfType<NotFoundObjectResult>();
        db.Set<PushReceipt>().Should().BeEmpty();
    }
    [Fact]
    public async Task InboxExigeAssinaturaEFiltraDestinatarioEValidade()
    {
        var id = await Dispatch();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherId = Convert.ToHexStringLower(SHA256.HashData(other.ExportSubjectPublicKeyInfo()));
        await Api.Register(SignDevice(new { kind = "cdb-installation-v1", installationId = otherId, token = "outro", apkVersion = "0.6.0", issuedAt = Now }, other), default);
        var empty = (OkObjectResult)await Api.Inbox(SignDevice(new { kind = "cdb-inbox-v1", installationId = otherId, issuedAt = Now }, other), default);
        JsonSerializer.SerializeToElement(empty.Value).GetProperty("messages").GetArrayLength().Should().Be(0);
        var own = (OkObjectResult)await Api.Inbox(SignDevice(new { kind = "cdb-inbox-v1", installationId = DeviceId, issuedAt = Now }), default);
        JsonSerializer.SerializeToElement(own.Value).GetProperty("messages").GetArrayLength().Should().Be(1);
        db.Set<PushMessage>().Single(x => x.Id == id).ExpiresAt = Now - 1; await db.SaveChangesAsync();
        var expired = (OkObjectResult)await Api.Inbox(SignDevice(new { kind = "cdb-inbox-v1", installationId = DeviceId, issuedAt = Now }), default);
        JsonSerializer.SerializeToElement(expired.Value).GetProperty("messages").GetArrayLength().Should().Be(0);
    }
    [Fact]
    public void ContratoRejeitaJsonDuplicadoBase64NaoCanonicoECurvaDiferente()
    {
        var duplicate = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"kind\":\"a\",\"kind\":\"b\"}"));
        Action parse = () => PushSignature.Payload(new(duplicate, "AA==")); parse.Should().Throw<FormatException>();
        Action base64 = () => PushSignature.Decode(" AA==", 10); base64.Should().Throw<FormatException>();
        using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var envelope = SignDevice(new { kind = "test" }, wrong);
        Action curve = () => PushSignature.Device(envelope, envelope.PublicKeyBase64!, Convert.ToHexStringLower(SHA256.HashData(wrong.ExportSubjectPublicKeyInfo())));
        curve.Should().Throw<UnauthorizedAccessException>();
    }
    [Fact]
    public void RateLimitDeRecibosNaoAfrouxaRegistroOuPublicacao()
    {
        foreach (var action in new[] { "Register", "Dispatch", "Control" })
            typeof(PushReceiptsController).GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("mobile-anonymous");
        foreach (var action in new[] { "Inbox", "Receipt" })
            typeof(PushReceiptsController).GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("mobile-push-receipts");
        typeof(PushReceiptsController).GetCustomAttribute<RequestSizeLimitAttribute>().Should().NotBeNull();
        EasyStock.Api.Authorization.ModulosConvention.Classificado(typeof(PushReceiptsController)).Should().BeTrue();
        EasyStock.Api.Authorization.ModulosConvention.ModulosDe(typeof(PushReceiptsController), "Receipt").Should().BeNull();
    }
    public void Dispose() { db.Dispose(); device.Dispose(); publisher.Dispose(); }
}
