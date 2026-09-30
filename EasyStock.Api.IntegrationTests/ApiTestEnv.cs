namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Aplica as chaves lidas em BUILD-TIME pelo <c>Program.cs</c> (connection string em
/// <c>builder.Configuration.GetConnectionString</c>; Jwt/Mobile no <c>StartupHardening</c>)
/// como env vars de PROCESSO — o unico canal que o Program.cs enxerga ANTES de
/// <c>builder.Build()</c>. O <c>ConfigureAppConfiguration</c> in-memory dos testes chega
/// tarde demais e e sobrescrito pelo appsettings (a connection string saia null,
/// AddNpgSql quebrava — issue 261).
///
/// <para>
/// Cada <c>CriarFactory</c> chama <see cref="Aplicar"/> LOGO antes de
/// <c>new WebApplicationFactory</c>. Com execucao serial (<c>DisableTestParallelization</c>
/// no AssemblyInfo) cada factory le o valor recem-setado, entao nao ha contaminacao de
/// connection string entre classes — a raiz das falhas do dry-run do #824 (env var de uma
/// classe vazando para as seguintes).
/// </para>
/// </summary>
public static class ApiTestEnv
{
    public const string JwtIssuer = "EasyStock";
    public const string JwtAudience = "EasyStock";
    public const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";
    public const string MobileApiKey = "easystock-integration-test-mobile-key-0001";

    /// <summary>Aponta as env vars build-time para <paramref name="connString"/> (nao-default).</summary>
    public static void Aplicar(string connString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "localhost:6379");
        Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
        Environment.SetEnvironmentVariable("RunMigrationsOnStartup", "true");
        Environment.SetEnvironmentVariable("Mobile__ApiKey", MobileApiKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtSecret);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("FileStorage__Provider", "Local");
        Environment.SetEnvironmentVariable("Anthropic__Enabled", "false");
    }
}
