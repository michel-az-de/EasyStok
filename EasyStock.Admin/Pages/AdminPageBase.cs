using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyStock.Admin.Pages;

public abstract class AdminPageBase(AdminSessionService session) : PageModel
{
    protected AdminSessionService Session => session;

    protected void SetSucesso(string mensagem) => TempData["Sucesso"] = mensagem;
    protected void SetErro(string mensagem)    => TempData["Erro"] = mensagem;

    /// <summary>
    /// Exibe erro amigável: ApiException.Message já é seguro; outros erros
    /// recebem mensagem genérica para não vazar detalhes técnicos ao operador.
    /// Também loga via ILoggerFactory pra forensics (best-effort — falha de
    /// log nunca derruba o handler).
    /// </summary>
    protected void SetErroSeguro(Exception ex, string contexto = "Operação")
    {
        // Log first — UX (toast) sai mesmo se o logger falhar.
        try
        {
            var loggerFactory = HttpContext?.RequestServices?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            loggerFactory?.CreateLogger(GetType()).LogError(ex,
                "[{Contexto}] {ExceptionType}: {Mensagem}", contexto, ex.GetType().Name, ex.Message);
        }
        catch { /* logging best-effort — sem fallback */ }

        if (ex is ApiException api)
            SetErro(api.Message);
        else
            SetErro($"{contexto} falhou. Tente novamente — se persistir, contate o suporte.");
    }

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            context.Result = new RedirectToPageResult("/Auth/Login");
            return;
        }

        // Validação adicional: token tem claim nivel autorizado para esta página?
        // PermiteNivelAdmin=true → cardápio/pages do tenant; false → SuperAdmin apenas.
        var autorizado = PermiteNivelAdmin
            ? IsSuperAdmin(token) || IsAdminComEmpresa(token)
            : IsSuperAdmin(token);

        if (!autorizado)
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
                var msg = ex is ApiException apiEx
                    ? apiEx.Message
                    : "Operação falhou. Tente novamente — se persistir, contate o suporte.";
                ((Microsoft.AspNetCore.Mvc.RazorPages.PageModel)context.HandlerInstance).TempData["Erro"] = msg;
            }
            catch { /* TempData não disponível em contextos especiais — ignora */ }
            context.Result = new RedirectToPageResult(context.ActionDescriptor.ViewEnginePath);
        }
    }

    /// <summary>
    /// Quando true, esta página aceita NivelAcesso.Admin (tenant) além de SuperAdmin.
    /// Override nas pages de cardápio (ADR-0031). Default: false (SuperAdmin apenas).
    /// </summary>
    protected virtual bool PermiteNivelAdmin => false;

    /// <summary>
    /// Decodifica o payload do JWT (segunda parte, base64url) e checa claim
    /// nivel == SuperAdmin. Sem validação de assinatura — defesa em profundidade.
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

    /// <summary>
    /// Checa se o token tem nivel == Admin E empresaId preenchido.
    /// Usado em páginas tenant (PermiteNivelAdmin = true).
    /// </summary>
    private static bool IsAdminComEmpresa(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return false;
            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            var nivel = doc.RootElement.TryGetProperty("nivel", out var n) ? n.GetString() : null;
            var empresaId = doc.RootElement.TryGetProperty("empresaId", out var e) ? e.GetString() : null;
            return string.Equals(nivel, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(empresaId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extrai o empresaId do JWT armazenado na sessão.
    /// Retorna null para SuperAdmin (sem restrição de empresa).
    /// </summary>
    protected Guid? GetEmpresaIdFromToken()
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("empresaId", out var e)
                && Guid.TryParse(e.GetString(), out var id))
                return id;
            return null;
        }
        catch
        {
            return null;
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
