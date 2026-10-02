namespace EasyStock.Api.Configuration;

/// <summary>
/// Chaves de configuracao usadas em Program.cs e demais servicos da API.
/// Centraliza as magic strings para facilitar manutencao e evitar typos.
/// </summary>
public static class ConfigurationKeys
{
    // ── Database ─────────────────────────────────────────────────────────────
    public const string DatabaseProvider        = "Database:Provider";

    // ── Connection Strings ───────────────────────────────────────────────────
    public const string ConnectionDefault       = "DefaultConnection";
    public const string ConnectionRedis         = "Redis";

    // ── JWT ──────────────────────────────────────────────────────────────────
    public const string JwtSecretKey            = "Jwt:SecretKey";
    public const string JwtIssuer               = "Jwt:Issuer";
    public const string JwtAudience             = "Jwt:Audience";

    /// <summary>
    /// #1352: confere a cada requisição se o JWT foi emitido depois da última revogação de sessão do usuário.
    /// Padrão <c>true</c>; <c>false</c> desliga a checagem sem migração (rollback).
    /// </summary>
    public const string AuthSessoesRevogaveis   = "Auth:SessoesRevogaveis";

    // ── CORS ─────────────────────────────────────────────────────────────────
    public const string CorsAllowedOrigins      = "Cors:AllowedOrigins";

    // ── OpenTelemetry ────────────────────────────────────────────────────────
    public const string OtlpEndpoint            = "OpenTelemetry:OtlpEndpoint";

    // ── App configuration sections ───────────────────────────────────────────
    public const string SectionEasyStock        = "EasyStock";
    public const string SectionFileStorage      = "FileStorage";

    // ── Logging ──────────────────────────────────────────────────────────────
    public const string LogDirectory            = "LogSettings:LogDirectory";
}
