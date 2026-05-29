using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyStock.Admin.Pages.Auth;

public class LogoutModel(
    AdminApiClient api,
    AdminSessionService session,
    ILogger<LogoutModel> logger) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        // Revoga refresh token no servidor antes de limpar a sessao local.
        // Falha na revogacao nao bloqueia o logout — sessao local sai de qualquer
        // jeito; refresh apenas vence naturalmente.
        var refreshToken = session.GetRefreshToken();
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                await api.PostRawAsync("api/auth/logout", new { refreshToken });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao revogar refresh token no logout admin");
            }
        }

        session.ClearSession();
        return RedirectToPage("/Auth/Login");
    }
}
