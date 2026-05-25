namespace EasyStock.Admin.Services;

public class AdminSessionService(IHttpContextAccessor httpContextAccessor)
{
    private const string TokenKey = "admin_token";
    private const string RefreshTokenKey = "admin_refresh_token";
    private const string NomeKey = "admin_nome";
    private const string EmailKey = "admin_email";

    private ISession Session => httpContextAccessor.HttpContext!.Session;

    public void SetSession(string token, string refreshToken, string nome, string email)
    {
        Session.SetString(TokenKey, token);
        Session.SetString(RefreshTokenKey, refreshToken);
        Session.SetString(NomeKey, nome);
        Session.SetString(EmailKey, email);
    }

    public void SetTokens(string token, string refreshToken)
    {
        Session.SetString(TokenKey, token);
        Session.SetString(RefreshTokenKey, refreshToken);
    }

    public string? GetToken() => Session.GetString(TokenKey);
    public string? GetRefreshToken() => Session.GetString(RefreshTokenKey);
    public string? GetNome() => Session.GetString(NomeKey);
    public string? GetEmail() => Session.GetString(EmailKey);

    public void ClearSession()
    {
        Session.Remove(TokenKey);
        Session.Remove(RefreshTokenKey);
        Session.Remove(NomeKey);
        Session.Remove(EmailKey);
    }
}
