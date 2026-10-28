using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>Autenticação por assinatura, pré-pareamento e sem dados operacionais de empresas.</summary>
[ApiController, Route("api/mobile/push"), AllowAnonymous]
[RequestSizeLimit(24576)]
public class PushReceiptsController(EasyStockDbContext db) : ControllerBase
{
    protected virtual byte[] PublisherKey => PushSignature.PublisherKey();
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static Guid Id(JsonElement value, string name)
    {
        if (!Guid.TryParseExact(PushSignature.Text(value, name, 36), "D", out var id) || id == Guid.Empty) throw new FormatException("Identificador inválido.");
        return id;
    }
    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        try { return await operation(); }
        catch (UnauthorizedAccessException) { return Unauthorized(new { error = "Assinatura não autorizada." }); }
        catch (Exception error) when (error is FormatException or JsonException or CryptographicException or ArgumentException)
        { return BadRequest(new { error = "Envelope ou conteúdo inválido." }); }
        catch (DbUpdateException) { return Conflict(new { error = "Conflito de registro. Repita a operação sem alterar sua identidade." }); }
    }
    private async Task<PushInstallation> Authenticate(PushEnvelope envelope, JsonElement value, CancellationToken ct)
    {
        var installationId = PushSignature.Installation(value);
        var installation = await db.Set<PushInstallation>().SingleOrDefaultAsync(x => x.Id == installationId, ct)
            ?? throw new UnauthorizedAccessException();
        if (envelope.PublicKeyBase64 != null && envelope.PublicKeyBase64 != installation.PublicKey) throw new UnauthorizedAccessException();
        PushSignature.Device(envelope, installation.PublicKey, installationId);
        return installation;
    }

    [HttpPost("installations")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-anonymous")]
    public Task<IActionResult> Register([FromBody] PushEnvelope envelope, CancellationToken ct) => Execute(async () =>
    {
        using var document = PushSignature.Payload(envelope); var value = document.RootElement;
        PushSignature.Fields(value, "kind", "installationId", "token", "apkVersion", "issuedAt");
        PushSignature.Kind(value, "cdb-installation-v1");
        var id = PushSignature.Installation(value); var issuedAt = PushSignature.Number(value, "issuedAt");
        PushSignature.Fresh(issuedAt, Now);
        var publicKey = envelope.PublicKeyBase64 ?? throw new FormatException();
        PushSignature.Device(envelope, publicKey, id);
        var token = PushSignature.Text(value, "token", 4096);
        if (token.Any(char.IsWhiteSpace)) throw new FormatException();
        var version = PushSignature.Text(value, "apkVersion", 64);
        var hash = PushSignature.Hash(envelope.PayloadBase64);
        var installation = await db.Set<PushInstallation>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (installation != null)
        {
            if (installation.PublicKey != publicKey || issuedAt < installation.IssuedAt || (issuedAt == installation.IssuedAt && installation.PayloadHash != hash))
                return Conflict(new { error = "Registro antigo ou divergente." });
            if (issuedAt == installation.IssuedAt) return Ok(new { registered = true, serverTime = Now });
        }
        else
        {
            installation = new PushInstallation { Id = id, PublicKey = publicKey, RegisteredAt = Now };
            db.Add(installation);
        }
        installation.Token = token; installation.ApkVersion = version;
        installation.IssuedAt = issuedAt; installation.PayloadHash = hash; installation.LastSeenAt = Now;
        await db.SaveChangesAsync(ct);
        return Ok(new { registered = true, serverTime = Now });
    });

    [HttpPost("inbox")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-push-receipts")]
    public Task<IActionResult> Inbox([FromBody] PushEnvelope envelope, CancellationToken ct) => Execute(async () =>
    {
        using var document = PushSignature.Payload(envelope); var value = document.RootElement;
        PushSignature.Fields(value, "kind", "installationId", "issuedAt"); PushSignature.Kind(value, "cdb-inbox-v1");
        PushSignature.Fresh(PushSignature.Number(value, "issuedAt"), Now);
        var installation = await Authenticate(envelope, value, ct); var now = Now;
        var messages = await db.Set<PushMessage>().AsNoTracking().Where(x => x.InstallationId == installation.Id && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { payloadBase64 = x.PayloadBase64, signatureBase64 = x.SignatureBase64 }).ToListAsync(ct);
        installation.LastSeenAt = now; await db.SaveChangesAsync(ct);
        return Ok(new { messages, serverTime = now });
    });

    [HttpPost("dispatch")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-anonymous")]
    public Task<IActionResult> Dispatch([FromBody] PushEnvelope envelope, CancellationToken ct) => Execute(async () =>
    {
        PushSignature.Publisher(envelope, PublisherKey);
        using var document = PushSignature.Payload(envelope); var value = document.RootElement;
        PushSignature.Fields(value, "kind", "id", "createdAt", "expiresAt", "target", "nonce", "title", "body", "screen");
        PushSignature.Kind(value, "cdb-notice-v1");
        var id = Id(value, "id"); var target = PushSignature.Installation(value, "target");
        var createdAt = PushSignature.Number(value, "createdAt"); var expiresAt = PushSignature.Number(value, "expiresAt");
        PushSignature.Fresh(createdAt, Now);
        if (expiresAt <= createdAt || expiresAt <= Now || expiresAt - createdAt > 7 * 86400000L) throw new FormatException();
        var nonce = PushSignature.Text(value, "nonce", 128);
        if (!Regex.IsMatch(nonce, "^[A-Za-z0-9_-]{22,128}$")) throw new FormatException();
        _ = PushSignature.Text(value, "title", 100); _ = PushSignature.Text(value, "body", 1200);
        if (PushSignature.Text(value, "screen", 32) is not ("home" or "migration-guide")) throw new FormatException();
        var installation = await db.Set<PushInstallation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == target, ct);
        if (installation == null) return NotFound(new { error = "Instalação não cadastrada." });
        var existing = await db.Set<PushMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (existing != null && (existing.PayloadBase64 != envelope.PayloadBase64 || existing.SignatureBase64 != envelope.SignatureBase64))
            return Conflict(new { error = "Identificador já usado por outro aviso." });
        if (existing == null)
        {
            if (await db.Set<PushMessage>().AnyAsync(x => x.Nonce == nonce, ct)) return Conflict(new { error = "Nonce já utilizado." });
            db.Add(new PushMessage { Id = id, InstallationId = target, Nonce = nonce, CreatedAt = createdAt, ExpiresAt = expiresAt,
                PayloadBase64 = envelope.PayloadBase64, SignatureBase64 = envelope.SignatureBase64 });
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { messageId = id, targets = new[] { new { installationId = target, token = installation.Token } } });
    });

    [HttpPost("receipts")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-push-receipts")]
    public Task<IActionResult> Receipt([FromBody] PushEnvelope envelope, CancellationToken ct) => Execute(async () =>
    {
        using var document = PushSignature.Payload(envelope); var value = document.RootElement;
        PushSignature.Fields(value, "kind", "installationId", "messageId", "nonce", "event", "source", "occurredAt", "apkVersion");
        PushSignature.Kind(value, "cdb-receipt-v1");
        var installation = await Authenticate(envelope, value, ct); var messageId = Id(value, "messageId");
        var message = await db.Set<PushMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId && x.InstallationId == installation.Id, ct);
        if (message == null) return NotFound(new { error = "Aviso não encontrado para esta instalação." });
        if (PushSignature.Text(value, "nonce", 128) != message.Nonce) return Unauthorized(new { error = "Recibo divergente." });
        var receiptEvent = PushSignature.Text(value, "event", 16); var source = PushSignature.Text(value, "source", 8);
        if (receiptEvent is not ("received" or "opened") || source is not ("push" or "sync")) throw new FormatException();
        var occurredAt = PushSignature.Number(value, "occurredAt"); var version = PushSignature.Text(value, "apkVersion", 64);
        var key = $"{messageId:D}:{installation.Id}:{receiptEvent}:{source}";
        var existing = await db.Set<PushReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == key, ct);
        if (existing != null) return Ok(new { received = true, confirmed = true, confirmedAt = existing.ConfirmedAt, serverTime = Now });
        var receipt = new PushReceipt { Id = key, MessageId = messageId, InstallationId = installation.Id, Event = receiptEvent, Source = source,
            OccurredAt = occurredAt, ConfirmedAt = Now, ApkVersion = version, PayloadBase64 = envelope.PayloadBase64, SignatureBase64 = envelope.SignatureBase64 };
        db.Add(receipt); await db.SaveChangesAsync(ct);
        return Ok(new { received = true, confirmed = true, confirmedAt = receipt.ConfirmedAt, serverTime = Now });
    });

    [HttpPost("control")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-anonymous")]
    public Task<IActionResult> Control([FromBody] PushEnvelope envelope, CancellationToken ct) => Execute(async () =>
    {
        PushSignature.Publisher(envelope, PublisherKey);
        using var document = PushSignature.Payload(envelope); var value = document.RootElement;
        PushSignature.Kind(value, "cdb-push-control-v1"); PushSignature.Fresh(PushSignature.Number(value, "issuedAt"), Now);
        var requestId = Id(value, "requestId"); var action = PushSignature.Text(value, "action", 32);
        if (action == "installations")
        {
            PushSignature.Fields(value, "kind", "action", "issuedAt", "requestId");
            var installations = await db.Set<PushInstallation>().AsNoTracking().OrderByDescending(x => x.LastSeenAt).Take(1000)
                .Select(x => new { installationId = x.Id, apkVersion = x.ApkVersion, registeredAt = x.RegisteredAt, lastSeenAt = x.LastSeenAt }).ToListAsync(ct);
            return Ok(new { installations, serverTime = Now });
        }
        if (action == "report")
        {
            PushSignature.Fields(value, "kind", "action", "issuedAt", "requestId", "messageId");
            var id = Id(value, "messageId");
            var message = await db.Set<PushMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (message == null) return NotFound();
            var receipts = await db.Set<PushReceipt>().AsNoTracking().Where(x => x.MessageId == id).ToListAsync(ct);
            var providers = await db.Set<PushProviderEvent>().AsNoTracking().Where(x => x.MessageId == id).OrderBy(x => x.RecordedAt).ToListAsync(ct);
            var accepted = providers.FirstOrDefault(x => x.Status == "accepted");
            long? First(string evt, string? src = null) => receipts.Where(x => x.Event == evt && (src == null || x.Source == src)).Select(x => (long?)x.ConfirmedAt).Min();
            return Ok(new { messageId = id, target = message.InstallationId, providerStatus = accepted != null ? "accepted" : providers.LastOrDefault()?.Status ?? "not-recorded",
                providerMessageId = accepted?.ProviderMessageId, acceptedAt = (long?)accepted?.RecordedAt,
                receivedPushAt = First("received", "push"), receivedSyncAt = First("received", "sync"), openedAt = First("opened") });
        }
        if (action != "provider") throw new FormatException();
        PushSignature.Fields(value, "kind", "action", "issuedAt", "requestId", "messageId", "installationId", "status", "providerMessageId");
        var messageId = Id(value, "messageId"); var target = PushSignature.Installation(value);
        if (!await db.Set<PushMessage>().AnyAsync(x => x.Id == messageId && x.InstallationId == target, ct)) return NotFound();
        var status = PushSignature.Text(value, "status", 16);
        if (status is not ("accepted" or "failed")) throw new FormatException();
        var providerId = value.GetProperty("providerMessageId").ValueKind == JsonValueKind.Null ? "" : PushSignature.Text(value, "providerMessageId", 512);
        if (status == "accepted" && providerId.Length == 0) throw new FormatException();
        var hash = PushSignature.Hash(envelope.PayloadBase64);
        var previous = await db.Set<PushProviderEvent>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (previous != null) return previous.PayloadHash == hash ? Ok(new { recorded = true }) : Conflict(new { error = "RequestId divergente." });
        db.Add(new PushProviderEvent { Id = requestId, MessageId = messageId, InstallationId = target, Status = status,
            ProviderMessageId = providerId, PayloadHash = hash, RecordedAt = Now });
        await db.SaveChangesAsync(ct);
        return Ok(new { recorded = true });
    });
}
