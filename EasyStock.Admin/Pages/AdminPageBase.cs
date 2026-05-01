using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Pages;

public abstract class AdminPageBase(AdminSessionService session) : PageModel
{
    protected AdminSessionService Session => session;

    protected void SetSucesso(string mensagem) => TempData["Sucesso"] = mensagem;
    protected void SetErro(string mensagem)    => TempData["Erro"] = mensagem;

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            context.Result = new RedirectToPageResult("/Auth/Login");
            return;
        }

        // Validação adicional: token tem claim nivel=SuperAdmin? Login filtra,
        // mas é defesa em profundidade — se a claim sumiu/foi adulterada
        // (token comprometido, app migrado), revoga sessão.
        if (!IsSuperAdmin(token))
        {
            session.ClearSession();
            context.Result = new RedirectToPageResult("/Auth/Login");
            return;
        }

        try
        {
            await next();
        }
        catch (SessionExpiredException)
        {
            session.ClearSession();
            context.Result = new RedirectToPageResult("/Auth/Login");
        }
    }

    /// <summary>
    /// Decodifica o payload do JWT (segunda parte, base64url) e checa claim
    /// nivel == SuperAdmin. Sem validação de assinatura — isso já é feito
    /// pela API quando o token é usado em chamadas downstream; aqui é apenas
    /// uma defesa em profundidade contra sessões com tokens não-admin.
    /// </summary>
    private static bool IsSuperAdmin(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return false;
            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            var nivel = doc.RootElement.TryGetProperty("nivel", out var n) ? n.GetString() : null;
            return string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
