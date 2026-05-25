using System.Reflection;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

[AllowAnonymous]
public class ErrorController(SessionService session) : Controller
{
    [Route("error/{code:int}")]
    public IActionResult Index(int code)
    {
        if (HttpContext.User.Identity?.IsAuthenticated == true && session.IsLoggedIn())
        {
            ViewBag.UsuarioNome = session.GetUsuarioNome();
            ViewBag.LojaAtualId = session.GetLojaId();
            ViewBag.LojaAtualNome = session.GetLojaNome();
            ViewBag.LojaAtualEmoji = session.GetLojaEmoji() ?? "🏪";
            ViewBag.Role = session.GetUsuarioRole();
            ViewBag.UserTheme = session.GetTemaPreferido();
        }

        // ErrorController não herda de BaseController, então AppVersion ficava null
        // e a sidebar caía no fallback "v1.0", divergindo das outras páginas (v1.10).
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        ViewBag.AppVersion = version is not null ? $"v{version.Major}.{version.Minor}" : "v1.0";

        if (code == 404)
            return View("NotFound");

        return View("Error");
    }
}
