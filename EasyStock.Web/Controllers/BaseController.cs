using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Web.Controllers;

[Authorize]
public abstract class BaseController(SessionService session) : Controller
{
    protected void Toast(string type, string message) =>
        TempData["Toast"] = $"{type}|{message}";

    protected bool HasError<T>(ApiResult<T> result)
    {
        if (!result.Success)
        {
            Toast("error", result.ErrorMessage ?? "Ocorreu um erro inesperado.");
            return true;
        }
        return false;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ViewBag.UsuarioNome = session.GetUsuarioNome();
        ViewBag.EmpresaId = session.GetEmpresaId();
        ViewBag.LojaAtualId = session.GetLojaId();
        ViewBag.LojaAtualNome = session.GetLojaNome();
        ViewBag.LojaAtualEmoji = session.GetLojaEmoji() ?? "🏪";
        ViewBag.Role = session.GetUsuarioRole();
        base.OnActionExecuting(context);
    }
}
