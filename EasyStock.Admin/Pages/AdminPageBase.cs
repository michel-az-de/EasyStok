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
        catch (Exception ex)
        {
            // Last-resort: qualquer exception não capturada por handlers individuais
            // vira página de erro amigável em vez de "HTTP 500". Logamos pra forensics.
            // Sem isso, exceções de rede (API down) ou bugs novos viram 500 cru no
            // browser do operador — UX terrível, especialmente em prod.
            try
            {
                var logger = context.HttpContext.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                logger?.CreateLogger("AdminPageBase").LogError(ex,
                    "Exceção não tratada no handler {Handler} da página {Page}",
                    context.HandlerMethod?.MethodInfo.Name ?? "(?)",
                    context.ActionDescriptor.ViewEnginePath);
            }
            catch { /* logging falhou — sem fallback */ }

            // TempData["Erro"] vai ser exibido como toast no _Layout. RedirectToPage
            // pra mesma rota mantém o usuário no contexto e mostra a mensagem amigável.
            try { context.HttpContext.Items["__handler_error"] = ex.Message; } catch { }
            try
            {
                ((Microsoft.AspNetCore.Mvc.RazorPages.PageModel)context.HandlerInstance).TempData["Erro"]
                    = "Operação falhou: " + (ex.Message?.Length > 200 ? ex.Message.Substring(0, 200) + "…" : ex.Message);
            }
            catch { /* TempData não disponível em contextos especiais — ignora */ }
            context.Result = new RedirectToPageResult(context.ActionDescriptor.ViewEnginePath);
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
