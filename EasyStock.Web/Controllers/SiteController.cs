using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Paginas publicas do EasyStok (app, contato, termos, privacidade). Todas as actions
/// sao anonimas, com layout proprio (_LayoutSite) — separado do app autenticado.
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
    /// <summary>
    /// A landing de venda e a pagina de precos sairam na poda P02 (billing SaaS).
    /// A raiz leva direto ao portal ou ao login.
    /// </summary>
    [HttpGet("")]
    public IActionResult Index() => session.IsLoggedIn()
        ? RedirectToAction("Index", "Launcher")
        : RedirectToAction("Login", "Auth");

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
