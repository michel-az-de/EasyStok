using System.Text.Json;

namespace EasyStock.Web.Services;

/// <summary>
/// Login em duas etapas (ADR-0047): estado que vive ENTRE o passo 1 (validar credenciais
/// e listar empresas) e o passo 2 (autenticar na empresa escolhida). Existe porque a Api
/// exige as credenciais de novo no passo 2, junto do <c>empresaId</c>.
///
/// <para>
/// Mora na sessao SERVER-SIDE — a mesma superficie que ja guarda o access e o refresh
/// token; ao cliente vai apenas o id de sessao, em cookie HttpOnly/Strict/Secure. Tem
/// validade curta propria e e removida no primeiro uso, com sucesso ou sem.
/// </para>
/// </summary>
public sealed record LoginPendente(
    string Email,
    string Senha,
    bool ManterLogado,
    string? ReturnUrl,
    IReadOnlyList<EmpresaLoginItem> Empresas,
    DateTime ExpiraEmUtc)
{
    public bool Expirou(DateTime agoraUtc) => agoraUtc >= ExpiraEmUtc;
}

public sealed record EmpresaLoginItem(string Id, string Nome);

public class SessionService(IHttpContextAccessor acc)
{
    /// <summary>Janela do login em duas etapas: tempo de escolher a empresa, nao de trabalhar.</summary>
    public static readonly TimeSpan LoginPendenteTtl = TimeSpan.FromMinutes(5);

    private const string KeyLoginPendente = "login_pendente";

    private ISession Session => acc.HttpContext!.Session;

    public string? GetToken() => Session.GetString("access_token");
    public string? GetRefreshToken() => Session.GetString("refresh_token");
    public string? GetLojaId() => Session.GetString("loja_atual_id");
    public string? GetLojaNome() => Session.GetString("loja_atual_nome");
    public string? GetLojaEmoji() => Session.GetString("loja_atual_emoji");
    public string? GetEmpresaId() => Session.GetString("empresa_atual_id");
    public string? GetUsuarioId() => Session.GetString("usuario_id");
    public string? GetUsuarioNome() => Session.GetString("usuario_nome");
    public string? GetUsuarioRole() => Session.GetString("usuario_role");
    public string GetTemaPreferido() => Session.GetString("usuario_tema") ?? "light";
    public bool IsLoggedIn() => !string.IsNullOrEmpty(GetToken());

    public void SetTokens(string accessToken, string refreshToken)
    {
        Session.SetString("access_token", accessToken);
        Session.SetString("refresh_token", refreshToken);
    }

    public void SetUsuario(string id, string nome, string role)
    {
        Session.SetString("usuario_id", id);
        Session.SetString("usuario_nome", nome);
        Session.SetString("usuario_role", role);
    }

    public void SetTemaPreferido(string? tema)
    {
        Session.SetString("usuario_tema", string.Equals(tema, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light");
    }

    public void SetEmpresaId(string empresaId)
    {
        Session.SetString("empresa_atual_id", empresaId);
    }

    public void SetLoja(string id, string nome, string? emoji, string? empresaId = null)
    {
        Session.SetString("loja_atual_id", id);
        Session.SetString("loja_atual_nome", nome);
        Session.SetString("loja_atual_emoji", emoji ?? "🏪");
        if (!string.IsNullOrEmpty(empresaId))
            Session.SetString("empresa_atual_id", empresaId);
    }

    public void SetLoginPendente(LoginPendente pendente)
    {
        Session.SetString(KeyLoginPendente, JsonSerializer.Serialize(pendente));
    }

    /// <summary>Pendencia valida, ou null se nao existe, esta corrompida ou expirou.</summary>
    public LoginPendente? GetLoginPendente(DateTime agoraUtc)
    {
        var raw = Session.GetString(KeyLoginPendente);
        if (string.IsNullOrEmpty(raw)) return null;

        LoginPendente? pendente;
        try { pendente = JsonSerializer.Deserialize<LoginPendente>(raw); }
        catch (JsonException) { pendente = null; }

        if (pendente is null || pendente.Expirou(agoraUtc))
        {
            LimparLoginPendente();
            return null;
        }

        return pendente;
    }

    /// <summary>
    /// Remove a pendencia (e a senha com ela). Chamado no primeiro uso — com sucesso ou
    /// sem — e ao voltar para a tela de login, para a credencial nao ficar em memoria
    /// alem do necessario.
    /// </summary>
    public void LimparLoginPendente()
    {
        Session.Remove(KeyLoginPendente);
    }

    public void Clear()
    {
        Session.Clear();
    }
}
