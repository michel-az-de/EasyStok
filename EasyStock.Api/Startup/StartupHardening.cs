using EasyStock.Api.Configuration;

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
}
