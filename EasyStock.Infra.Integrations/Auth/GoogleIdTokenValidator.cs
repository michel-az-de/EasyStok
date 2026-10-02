using EasyStock.Application.Ports.Output.Auth;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Auth;

/// <summary>Seção <c>Auth:Google</c> (#1324). Sem <see cref="ClientId"/>, o login com Google fica desligado.</summary>
public sealed class GoogleAuthOptions
{
    public const string Secao = "Auth:Google";

    /// <summary>ID do cliente OAuth (Web) do Google Cloud. É público: vai também para o console.</summary>
    public string? ClientId { get; set; }

    /// <summary>CNPJ ou nome da empresa padrão do superadmin no console (#1326).</summary>
    public string? EmpresaPadrao { get; set; }
}

/// <summary>
/// Valida o ID token do Google Identity Services com a biblioteca oficial: assinatura pelas chaves públicas
/// do Google, emissor, validade e audiência igual ao <see cref="GoogleAuthOptions.ClientId"/>.
/// </summary>
public sealed class GoogleIdTokenValidator(IOptions<GoogleAuthOptions> options, ILogger<GoogleIdTokenValidator> logger)
    : IGoogleIdTokenValidator
{
    public string? ClientId => string.IsNullOrWhiteSpace(options.Value.ClientId) ? null : options.Value.ClientId.Trim();

    public string? EmpresaPadrao => string.IsNullOrWhiteSpace(options.Value.EmpresaPadrao) ? null : options.Value.EmpresaPadrao.Trim();

    public async Task<IdentidadeGoogle?> ValidarAsync(string idToken, CancellationToken ct = default)
    {
        if (ClientId is not { } clientId) return null;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            return string.IsNullOrWhiteSpace(payload.Email) ? null : new IdentidadeGoogle(payload.Email, payload.EmailVerified);
        }
        catch (InvalidJwtException ex)
        {
            // O token não vai para o log; só o motivo da recusa.
            logger.LogWarning("Login com Google recusado: {Motivo}", ex.Message);
            return null;
        }
    }
}
