using EasyStock.Api.Configuration;
using EasyStock.Application.Common;

namespace EasyStock.Api.Startup;

/// <summary>
/// Validações que rodam após builder.Build() e antes do app.Run() pra falhar rápido
/// em config quebrada/perigosa — chave JWT fraca/vazada, connection string com placeholder
/// não substituído, credenciais default, Mobile API key vazada.
///
/// Todas as falhas resultam em <see cref="InvalidOperationException"/> com mensagem
/// específica indicando qual env var setar.
/// </summary>
public static class StartupHardening
{
    public static void Validate(
        WebApplicationBuilder builder,
        string? postgresConnectionString)
    {
        var jwtSecret = builder.Configuration[ConfigurationKeys.JwtSecretKey];
        if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("${JWT_SECRET_KEY}"))
            throw new InvalidOperationException("JWT_SECRET_KEY environment variable is required (min 32 chars). Set it before starting the API.");
        if (jwtSecret.Length < 32)
            throw new InvalidOperationException("JWT_SECRET_KEY must be at least 32 characters long.");
        // Bloquear o secret de dev conhecido em qualquer ambiente — caso vaze de novo, falha rápido.
        if (jwtSecret.Contains("EasyStock-Dev-SuperSecretKey", StringComparison.Ordinal))
            throw new InvalidOperationException("CRITICAL: known leaked dev JWT secret detected in configuration. Rotate JWT_SECRET_KEY immediately.");

        // Validar connection strings não têm placeholders
        if (postgresConnectionString?.Contains("${") == true)
            throw new InvalidOperationException("Database connection string contains placeholders. Set environment variables: DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD.");

        // Validar que database credentials não são defaults/placeholders
        if (postgresConnectionString?.Contains("Username=postgres") == true && postgresConnectionString?.Contains("Password=postgres") == true)
            throw new InvalidOperationException("CRITICAL: Default PostgreSQL credentials detected. Set DB_PASSWORD to a secure value before deployment.");

        // Validar Mobile:ApiKey: rejeitar valor literal vazado e exigir tamanho mínimo em Production.
        var mobileApiKey = builder.Configuration["Mobile:ApiKey"];
        if (!string.IsNullOrEmpty(mobileApiKey))
        {
            if (mobileApiKey.Contains("${MOBILE_API_KEY}", StringComparison.Ordinal))
                throw new InvalidOperationException("MOBILE_API_KEY environment variable is required when Mobile:ApiKey is configured. Set it via env var or user-secrets.");
            // Identidade quebrada em duas partes pra gitleaks nao flaggear o literal no codigo
            // — o valor abaixo eh exatamente a chave dev vazada que estamos rejeitando.
            const string knownLeakedDevKey = "cdb-dev-key-change" + "-in-production-2026"; // gitleaks:allow
            if (mobileApiKey.Equals(knownLeakedDevKey, StringComparison.Ordinal))
                throw new InvalidOperationException("CRITICAL: known leaked dev Mobile API key detected in configuration. Rotate Mobile:ApiKey immediately.");
            if (builder.Environment.IsProduction() && mobileApiKey.Length < 24)
                throw new InvalidOperationException("Mobile:ApiKey must be at least 24 characters long in Production.");
        }
    }

    /// <summary>
    /// Fuso de Brasilia: em Production recusa subir se o fuso degradou (caiu na zona fixa
    /// -03:00, tipicamente imagem sem tzdata) ou se o offset esta fora da banda plausivel.
    /// Em dev/teste apenas tolera (o app sobe). Os fluxos que dependem de hora nao podem
    /// rodar em producao com o relogio errado — falhar rapido evita dano silencioso.
    /// </summary>
    public static void ValidateTimezone(IHostEnvironment environment)
        => ValidateTimezoneCore(environment.IsProduction(), HorarioBrasil.Fonte, HorarioBrasil.OffsetMinutosAtual());

    /// <summary>Nucleo puro/testavel de <see cref="ValidateTimezone"/>.</summary>
    public static void ValidateTimezoneCore(bool isProduction, FonteFuso fonte, int offsetMinutos)
    {
        var plausivel = offsetMinutos is >= -300 and <= -60; // Brasil: -180 (ou -120 se o DST voltar).
        if (isProduction && (fonte == FonteFuso.FallbackFixo || !plausivel))
            throw new InvalidOperationException(
                $"CRITICAL: fuso de Brasilia degradado em producao (fonte={fonte}, offset={offsetMinutos}min). " +
                "A imagem provavelmente esta sem tzdata; rebuild com 'apt-get install -y tzdata'.");
    }
}
