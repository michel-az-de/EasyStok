using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Authorization;

/// <summary>
/// Configuração do consumidor da fila de impressão (S20), seção <c>Impressao</c>. A chave vem de secret
/// store / env var (<c>Impressao__ApiKey</c>), nunca do appsettings. Sem chave, só o console (JWT) consome.
/// </summary>
public sealed class ImpressaoOptions
{
    public const string Section = "Impressao";

    /// <summary>Chave do bridge local, enviada no header <c>X-Impressao-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Empresa cuja fila o bridge consome. Uma chave = uma empresa.</summary>
    public Guid? EmpresaId { get; set; }
}

/// <summary>
/// Autentica o bridge de impressão (S20) pelo header <c>X-Impressao-Api-Key</c>. O principal leva a claim
/// <c>empresaId</c> da empresa configurada, então <see cref="ICurrentUserAccessor"/>, o filtro global de
/// tenant e o RLS enxergam só a fila dela. O scheme só entra em ação nas rotas da policy
/// <see cref="PolicyFila"/>; nas demais o default continua JWT. Falha sempre vira 401.
/// </summary>
public sealed class ImpressaoApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<ImpressaoOptions> impressaoOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ImpressaoApiKey";
    public const string PolicyFila = "ImpressaoFila";
    public const string HeaderName = "X-Impressao-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var valores) || string.IsNullOrWhiteSpace(valores.ToString()))
            return Task.FromResult(AuthenticateResult.NoResult());

        var config = impressaoOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(config.ApiKey) || config.EmpresaId is not { } empresaId || empresaId == Guid.Empty)
        {
            Logger.LogWarning("ImpressaoApiKey: Impressao:ApiKey ou Impressao:EmpresaId não configurado — rejeitando.");
            return Task.FromResult(AuthenticateResult.Fail("Consumidor de impressão não configurado no servidor."));
        }

        if (!IguaisEmTempoConstante(valores.ToString().Trim(), config.ApiKey))
        {
            Logger.LogWarning("ImpressaoApiKey: chave inválida — IP={RemoteIp}",
                Context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return Task.FromResult(AuthenticateResult.Fail("Chave inválida."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "impressao-bridge"),
            new Claim("empresaId", empresaId.ToString()),
            new Claim("scheme", SchemeName),
        ], SchemeName, "sub", "nivel");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private static bool IguaisEmTempoConstante(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

public static class ImpressaoApiKeyAuthExtensions
{
    private static readonly string[] NiveisOperador = ["SuperAdmin", "Admin", "Gerente", "Operador"];

    public static AuthenticationBuilder AddImpressaoApiKeyScheme(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        builder.Services.Configure<ImpressaoOptions>(configuration.GetSection(ImpressaoOptions.Section));
        return builder.AddScheme<AuthenticationSchemeOptions, ImpressaoApiKeyAuthHandler>(
            ImpressaoApiKeyAuthHandler.SchemeName, _ => { });
    }

    /// <summary>
    /// Policy da fila: operador do console (JWT, mesmo conjunto da policy <c>Operador</c>) <b>ou</b> o bridge
    /// autenticado pela chave.
    /// </summary>
    public static AuthorizationOptions AddImpressaoFilaPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(ImpressaoApiKeyAuthHandler.PolicyFila, policy =>
        {
            policy.AuthenticationSchemes = [JwtBearerDefaults.AuthenticationScheme, ImpressaoApiKeyAuthHandler.SchemeName];
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx =>
                ctx.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == ImpressaoApiKeyAuthHandler.SchemeName) ||
                ctx.User.Claims.Any(c => c.Type == "nivel" && NiveisOperador.Contains(c.Value)));
        });
        return options;
    }
}
