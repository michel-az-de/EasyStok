namespace EasyStock.Api.Configuration;

/// <summary>
/// Chaves de configuracao usadas em Program.cs e demais servicos da API.
/// Centraliza as magic strings para facilitar manutencao e evitar typos.
/// </summary>
public static class ConfigurationKeys
{
    // ── Database ─────────────────────────────────────────────────────────────
    public const string DatabaseProvider        = "Database:Provider";
    public const string DatabaseMongoDatabase   = "Database:MongoDatabase";

    // ── Connection Strings ───────────────────────────────────────────────────
    public const string ConnectionDefault       = "DefaultConnection";
    public const string ConnectionMongo         = "MongoConnection";
    public const string ConnectionSqlite        = "SqliteConnection";
    public const string ConnectionRedis         = "Redis";

    // ── JWT ──────────────────────────────────────────────────────────────────
    public const string JwtSecretKey            = "Jwt:SecretKey";
    public const string JwtIssuer               = "Jwt:Issuer";
    public const string JwtAudience             = "Jwt:Audience";

    // ── CORS ─────────────────────────────────────────────────────────────────
    public const string CorsAllowedOrigins      = "Cors:AllowedOrigins";

    // ── OpenTelemetry ────────────────────────────────────────────────────────
    public const string OtlpEndpoint            = "OpenTelemetry:OtlpEndpoint";

    // ── App configuration sections ───────────────────────────────────────────
    public const string SectionEasyStock        = "EasyStock";
    public const string SectionFileStorage      = "FileStorage";
}
