using EasyStock.Application.Ports.Output.Auth;

namespace EasyStock.Application.UseCases.AutenticarUsuario;

/// <summary>
/// #1324: troca o ID token do Google pelo usuário do EasyStok que já existe com esse e-mail. Não cria conta.
/// No Gmail, <c>nome+qualquer@gmail.com</c> chega na mesma caixa de <c>nome@gmail.com</c>; por isso o alias
/// casa com a conta Google base quando há uma única conta com esse alias.
/// </summary>
public class IdentificarUsuarioGoogleUseCase(IGoogleIdTokenValidator validador, IUsuarioRepository usuarios)
{
    private static readonly string[] DominiosGmail = ["gmail.com", "googlemail.com"];

    public async Task<Usuario> ExecuteAsync(string idToken, CancellationToken ct = default)
    {
        var identidade = string.IsNullOrWhiteSpace(idToken) ? null : await validador.ValidarAsync(idToken, ct);
        if (identidade is null || !identidade.EmailVerificado)
            throw new CredenciaisInvalidasException("Conta Google não confirmada.");

        var email = identidade.Email.Trim().ToLowerInvariant();
        var usuario = await usuarios.GetByEmailAsync(email) ?? await PorAliasGmailAsync(email);

        if (usuario is null || !usuario.Ativo)
            throw new CredenciaisInvalidasException("Esta conta Google não tem acesso ao EasyStok.");
        if (usuario.EstaBloqueado())
            throw new CredenciaisInvalidasException("Conta bloqueada temporariamente.");
        return usuario;
    }

    private async Task<Usuario?> PorAliasGmailAsync(string email)
    {
        var arroba = email.LastIndexOf('@');
        if (arroba <= 0 || !DominiosGmail.Contains(email[(arroba + 1)..]) || email[..arroba].Contains('+'))
            return null;

        var candidatos = await usuarios.ListarPorAliasGmailAsync(email[..arroba]);
        return candidatos.Count == 1 ? candidatos[0] : null;
    }
}
