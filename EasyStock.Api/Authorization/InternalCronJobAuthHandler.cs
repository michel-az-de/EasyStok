using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Authorization;

/// <summary>
/// Esquema de autenticação para endpoints internos de cron job.
/// Valida header <c>X-Internal-Cron-Token</c> contra <c>Notifications:CronJob:Token</c>
/// (que SEMPRE deve vir de secret store / env var, NUNCA do appsettings.json em prod).
/// <para>
/// Status code retornado: <b>401</b> em qualquer cenário de falha de autenticação
/// (sem header, header vazio, token inválido, endpoints desabilitados, token do servidor
/// não configurado). O handler usa apenas <c>AuthenticateResult.NoResult</c>
/// e <c>AuthenticateResult.Fail</c> — não há requirement adicional pós-autenticação
/// que justifique 403.
/// </para>
/// <para>
/// Tentativas com token inválido são logadas como <c>Warning</c> com IP da origem
/// para auditoria de segurança (detecção de força bruta).
/// </para>
/// Habilitação controlada por <c>Notifications:CronJob:Habilitado</c> (default false).
/// </summary>
public sealed class InternalCronJobAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InternalCronJob";
    public const string PolicyName = "InternalCronJob";
    private const string HeaderName = "X-Internal-Cron-Token";

    private readonly IConfiguration _configuration;

    public InternalCronJobAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, loggerFactory, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var habilitado = _configuration.GetValue<bool>("Notifications:CronJob:Habilitado");
        if (!habilitado)
            return Task.FromResult(AuthenticateResult.Fail("Cron job endpoints desabilitados (Notifications:CronJob:Habilitado=false)."));

        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedToken = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(providedToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var expectedToken = _configuration["Notifications:CronJob:Token"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            Logger.LogWarning("InternalCronJob: token configurado vazio — rejeitando requisição.");
            return Task.FromResult(AuthenticateResult.Fail("Token do cron job não configurado no servidor."));
        }

        // Comparação tempo-constante para evitar timing attacks.
        if (!CryptographicEquals(providedToken, expectedToken))
        {
            // Auditoria de segurança — registra tentativa de força bruta com origem.
            var remoteIp = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            Logger.LogWarning(
                "InternalCronJob: tentativa de autenticação rejeitada (token inválido) — IP={RemoteIp}",
                remoteIp);
            return Task.FromResult(AuthenticateResult.Fail("Token inválido."));
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "internal-cron-job"), new Claim("scheme", SchemeName) },
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
