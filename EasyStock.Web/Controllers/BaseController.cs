using System.Reflection;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Web.Controllers;

[Authorize]
public abstract class BaseController(SessionService session) : Controller
{
    /// <summary>
    /// Sessão exposta para subclasses que precisam ler EmpresaId/LojaId/UsuarioNome.
    /// Subclasses devem usar <c>Session</c> (não recapturar SessionService no construtor primário,
    /// que provoca CS9107 — duplicação de captura entre base e derivado).
    /// </summary>
    protected SessionService Session => session;

    // Controllers que podem ser acessados mesmo sem loja selecionada — usuário precisa
    // chegar até a tela de Lojas para criar a primeira, e até Assinatura caso o trial
    // tenha expirado. A lista é mantida pequena de propósito.
    private static readonly HashSet<string> ControllersAllowedWithoutLoja = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lojas",
        "Assinatura",
        // KDS filtra por empresaId (não lojaId) — funciona sem loja selecionada.
        // Sem esta entrada, login com múltiplas lojas redireciona para SelecionarLoja
        // antes que o usuário possa acessar a tela de cozinha.
        "Kds"
    };

    protected bool IsAdmin()
    {
        var role = session.GetUsuarioRole() ?? string.Empty;
        return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase);
    }

    protected bool IsGerente()
    {
        var role = session.GetUsuarioRole() ?? string.Empty;
        return IsAdmin()
            || role.Equals("Gerente", StringComparison.OrdinalIgnoreCase);
    }

    protected void Toast(string type, string message, string? undoUrl = null) =>
        TempData["Toast"] = undoUrl is not null ? $"{type}|{message}|{undoUrl}" : $"{type}|{message}";

    protected void ToastError(string message, string? correlationId = null) =>
        TempData["Toast"] = correlationId is not null
            ? $"error|{message}||{correlationId}"
            : $"error|{message}";

    protected bool HasError<T>(ApiResult<T> result)
    {
        if (!result.Success)
        {
            ToastError(
                UserFacingErrors.Sanitize(result.ErrorCode, result.ErrorMessage, result.HttpStatus),
                result.CorrelationId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Igual a <see cref="HasError"/>, mas expõe code+HTTP+mensagem crua no toast.
    /// Use em operações de escrita (POST/PATCH/DELETE) onde "redirect silencioso"
    /// dá a impressão de "clicou e nada aconteceu". O end-user precisa saber se
    /// foi 401 (sessão), 403 (empresa), 404 (recurso) ou 400 (validação).
    /// </summary>
    protected bool HasErrorVerbose<T>(ApiResult<T> result, string acao)
    {
        if (result.Success) return false;
        var code = result.ErrorCode ?? "?";
        var msg = result.ErrorMessage ?? "Erro ao processar requisição.";
        Toast("error", $"{acao} falhou ({code} · HTTP {result.HttpStatus}): {msg}");
        return true;
    }

    /// <summary>
    /// Retorna um redirect para <paramref name="action"/> (padrão: Index) caso o resultado
    /// da API seja erro ou dado nulo. Retorna <c>null</c> se o resultado for bem-sucedido,
    /// permitindo o pattern: <c>if (RedirectIfError(r) is { } red) return red;</c>
    /// </summary>
    protected IActionResult? RedirectIfError<T>(ApiResult<T> result, string? action = null)
        where T : class
    {
        if (!HasError(result) && result.Data is not null) return null;
        return RedirectToAction(action ?? nameof(Index));
    }

    /// <summary>
    /// Resposta JSON de sucesso para endpoints AJAX. Formato: <c>{ ok: true }</c>.
    /// Use <c>JsonOk(new { id })</c> para incluir dados adicionais.
    /// </summary>
    protected JsonResult JsonOk(object? extra = null) =>
        Json(extra is not null
            ? new { ok = true, data = extra }
            : (object)new { ok = true });

    /// <summary>
    /// Resposta JSON de erro para endpoints AJAX. Formato: <c>{ ok: false, erro: "..." }</c>.
    /// </summary>
    protected JsonResult JsonFail(string message) =>
        Json(new { ok = false, erro = message });

    protected IActionResult? RedirectIfLimitReached<T>(ApiResult<T> result)
    {
        if (result.Success) return null;
        var code = result.ErrorCode ?? string.Empty;
        if (!code.StartsWith("LIMITE_PLANO")) return null;
        var recurso = code.Contains(':') ? code[(code.IndexOf(':') + 1)..] : null;
        TempData["UpgradeLimite"] = recurso ?? "recurso";
        return RedirectToAction("Index", "Assinatura");
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            TempData["Toast"] = "warning|Sessão expirada. Faça login novamente.";
            context.Result = RedirectToAction("Login", "Auth");
            return;
        }

        // Bloqueia acesso a recursos transacionais quando não há loja selecionada.
        // Sem loja, qualquer criação/edição cairia em estado inconsistente — empurra
        // o usuário para o fluxo de seleção/criação de loja.
        var lojaId = session.GetLojaId();
        if (string.IsNullOrEmpty(lojaId))
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;
            if (!ControllersAllowedWithoutLoja.Contains(controllerName))
            {
                TempData["Toast"] = "warning|Selecione ou cadastre uma loja para continuar.";
                context.Result = RedirectToAction("SelecionarLoja", "Auth");
                return;
            }
        }

        ViewBag.UsuarioNome = session.GetUsuarioNome();
        ViewBag.LojaAtualId = lojaId;
        ViewBag.LojaAtualNome = session.GetLojaNome();
        ViewBag.LojaAtualEmoji = session.GetLojaEmoji() ?? "🏪";
        ViewBag.Role = session.GetUsuarioRole();
        ViewBag.UserTheme = session.GetTemaPreferido();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        ViewBag.AppVersion = version is not null ? $"v{version.Major}.{version.Minor}" : "v1.0";
        base.OnActionExecuting(context);
    }
}
