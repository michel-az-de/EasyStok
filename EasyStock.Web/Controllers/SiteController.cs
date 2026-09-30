using EasyStock.Web.Models.ViewModels.Site;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Landing publica do EasyStok. Todas as actions sao anonimas, com layout
/// proprio (_LayoutSite) — separado do app autenticado. Usuarios ja logados
/// sao redirecionados pro portal (Launcher) pra nao verem pitch de venda novamente.
///
/// <para>
/// Antiforgery: o EasyStock.Web ja registra <see cref="AutoValidateAntiforgeryTokenAttribute"/>
/// como filtro global em Program.cs, entao todos os POST sao validados
/// automaticamente sem precisar de [ValidateAntiForgeryToken] explicito.
/// </para>
///
/// <para>
/// MarketingOptions e injetado via @inject IOptions&lt;MarketingOptions&gt;
/// no _LayoutSite, dispensando ViewBag.Marketing nas actions.
/// </para>
/// </summary>
[AllowAnonymous]
[Route("/")]
public sealed class SiteController(
    SessionService session) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        // Usuario ja autenticado vai direto pro portal — landing e pitch de venda.
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Launcher");

        return View(new LandingViewModel());
    }

    [HttpGet("precos")]
    public IActionResult Precos() => View();

    [HttpGet("app")]
    public IActionResult App() => View();

    /// <summary>
    /// Fale com a gente sem formulario: a captura de leads publicos saiu na
    /// poda P03 (#1116); a pagina aponta para e-mail e WhatsApp.
    /// </summary>
    [HttpGet("contato")]
    public IActionResult Contato() => View();

    [HttpGet("termos")]
    public IActionResult TermosDeUso() => View();

    [HttpGet("privacidade")]
    public IActionResult PoliticaPrivacidade() => View();
}
