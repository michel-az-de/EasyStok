namespace EasyStock.Web.Services;

public class SessionService(IHttpContextAccessor acc)
{
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

    public void Clear()
    {
        Session.Clear();
    }
}
