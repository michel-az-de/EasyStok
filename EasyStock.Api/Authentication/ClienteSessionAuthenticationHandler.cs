using System.Security.Claims;
using System.Text.Encodings.Web;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Authentication;

/// <summary>
/// Handler de autenticação para sessões server-side do storefront (ADR-0012).
///
/// <para>
/// Lê o cookie <c>__Host-cdb_session</c>, busca a <c>ClienteSession</c>
/// correspondente no banco, valida expiração e fingerprint (UA + Accept-Language),
/// e popula o <c>ClaimsPrincipal</c> com as claims da sessão.
/// </para>
///
/// <para>
/// Claims geradas: <c>sid</c> (session ID), <c>sub</c> (cliente ID),
/// <c>empresa_id</c> (empresa do tenant).
/// </para>
/// </summary>
public class ClienteSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ClienteSession";
    public const string CookieName = "__Host-cdb_session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var cookieValue)
            || string.IsNullOrWhiteSpace(cookieValue))
            return AuthenticateResult.NoResult();

        if (!Guid.TryParse(cookieValue, out var sessionId))
            return AuthenticateResult.Fail("Cookie de sessão inválido.");

        var sessionRepo = Context.RequestServices.GetRequiredService<IClienteSessionRepository>();
        var timeProvider = Context.RequestServices.GetRequiredService<TimeProvider>();

        var session = await sessionRepo.GetByIdAsync(sessionId);

        if (session is null)
            return AuthenticateResult.Fail("Sessão não encontrada.");

        if (!session.EstaValida(timeProvider))
            return AuthenticateResult.Fail("Sessão expirada ou revogada.");

        // Validação de fingerprint (anti-hijacking)
        var ua = Request.Headers.UserAgent.ToString();
        var al = Request.Headers["Accept-Language"].ToString();
        var fingerprintAtual = ClienteFingerprintCalculator.Calcular(ua, al);

        if (session.Fingerprint is not null && fingerprintAtual is not null
            && session.Fingerprint != fingerprintAtual)
        {
            Logger.LogWarning(
                "Fingerprint mismatch — possível session hijacking. sessionId={SessionId} clienteId={ClienteId}",
                session.Id, session.ClienteId);
            return AuthenticateResult.Fail("Fingerprint de sessão inválido.");
        }

        var claims = new[]
        {
            new Claim("sid", session.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, session.ClienteId.ToString()),
            new Claim("cliente_id", session.ClienteId.ToString()),
            new Claim("empresa_id", session.EmpresaId.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
