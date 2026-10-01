namespace EasyStock.Application.Ports.Output.Auth;

/// <summary>Identidade que o Google atesta no ID token (#1324).</summary>
public sealed record IdentidadeGoogle(string Email, bool EmailVerificado);

/// <summary>
/// Valida o ID token do Google Identity Services: assinatura, emissor, validade e cliente
/// (<c>Auth:Google:ClientId</c>). Token inválido devolve <c>null</c>.
/// </summary>
public interface IGoogleIdTokenValidator
{
    /// <summary>Nulo quando o login com Google está desligado (sem ClientId).</summary>
    string? ClientId { get; }

    /// <summary>#1326: slug da vitrine cuja empresa o superadmin recebe ao entrar pelo Google.</summary>
    string? EmpresaPadraoSlug { get; }

    Task<IdentidadeGoogle?> ValidarAsync(string idToken, CancellationToken ct = default);
}
