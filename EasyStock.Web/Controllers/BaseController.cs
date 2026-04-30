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
    protected void Toast(string type, string message, string? undoUrl = null) =>
        TempData["Toast"] = undoUrl is not null ? $"{type}|{message}|{undoUrl}" : $"{type}|{message}";

    protected bool HasError<T>(ApiResult<T> result)
    {
        if (!result.Success)
        {
            Toast("error", result.ErrorMessage ?? "Ocorreu um erro inesperado.");
            return true;
        }
        return false;
    }

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

        ViewBag.UsuarioNome = session.GetUsuarioNome();
        ViewBag.LojaAtualId = session.GetLojaId();
        ViewBag.LojaAtualNome = session.GetLojaNome();
        ViewBag.LojaAtualEmoji = session.GetLojaEmoji() ?? "🏪";
        ViewBag.Role = session.GetUsuarioRole();
        ViewBag.UserTheme = session.GetTemaPreferido();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        ViewBag.AppVersion = version is not null ? $"v{version.Major}.{version.Minor}" : "v1.0";
        base.OnActionExecuting(context);
    }
}
