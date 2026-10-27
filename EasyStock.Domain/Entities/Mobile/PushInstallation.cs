namespace EasyStock.Domain.Entities.Mobile;

// Identidade técnica pré-pareamento. Não representa pessoa, empresa ou permissão operacional.
[Table("mobile_push_installations")]
public sealed class PushInstallation
{
    [Key, MaxLength(64), Column("id")] public string Id { get; set; } = "";
    [MaxLength(512), Column("public_key")] public string PublicKey { get; set; } = "";
    [MaxLength(4096), Column("token")] public string Token { get; set; } = "";
    [MaxLength(64), Column("apk_version")] public string ApkVersion { get; set; } = "";
    [ConcurrencyCheck, Column("issued_at")] public long IssuedAt { get; set; }
    [MaxLength(64), Column("payload_hash")] public string PayloadHash { get; set; } = "";
    [Column("registered_at")] public long RegisteredAt { get; set; }
    [Column("last_seen_at")] public long LastSeenAt { get; set; }
}

[Table("mobile_push_messages")]
public sealed class PushMessage
{
    [Key, Column("id")] public Guid Id { get; set; }
    [MaxLength(64), Column("installation_id")] public string InstallationId { get; set; } = "";
    [MaxLength(128), Column("nonce")] public string Nonce { get; set; } = "";
    [Column("created_at")] public long CreatedAt { get; set; }
    [Column("expires_at")] public long ExpiresAt { get; set; }
    [Column("payload_base64")] public string PayloadBase64 { get; set; } = "";
    [Column("signature_base64")] public string SignatureBase64 { get; set; } = "";
}

[Table("mobile_push_receipts")]
public sealed class PushReceipt
{
    [Key, MaxLength(180), Column("id")] public string Id { get; set; } = "";
    [Column("message_id")] public Guid MessageId { get; set; }
    [MaxLength(64), Column("installation_id")] public string InstallationId { get; set; } = "";
    [MaxLength(16), Column("event")] public string Event { get; set; } = "";
    [MaxLength(8), Column("source")] public string Source { get; set; } = "";
    [Column("occurred_at")] public long OccurredAt { get; set; }
    [Column("confirmed_at")] public long ConfirmedAt { get; set; }
    [MaxLength(64), Column("apk_version")] public string ApkVersion { get; set; } = "";
    [Column("payload_base64")] public string PayloadBase64 { get; set; } = "";
    [Column("signature_base64")] public string SignatureBase64 { get; set; } = "";
}

[Table("mobile_push_provider_events")]
public sealed class PushProviderEvent
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("message_id")] public Guid MessageId { get; set; }
    [MaxLength(64), Column("installation_id")] public string InstallationId { get; set; } = "";
    [MaxLength(16), Column("status")] public string Status { get; set; } = "";
    [MaxLength(512), Column("provider_message_id")] public string ProviderMessageId { get; set; } = "";
    [MaxLength(64), Column("payload_hash")] public string PayloadHash { get; set; } = "";
    [Column("recorded_at")] public long RecordedAt { get; set; }
}
