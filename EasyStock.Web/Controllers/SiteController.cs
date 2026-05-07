using EasyStock.Web.Models.ViewModels.Site;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Landing publica do EasyStok. Todas as actions sao anonimas, com layout
/// proprio (_LayoutSite) — separado do app autenticado. Usuarios ja logados
/// sao redirecionados pro dashboard pra nao verem pitch de venda novamente.
/// </summary>
[AllowAnonymous]
[Route("/")]
public sealed class SiteController(
    LeadsApiService leadsApi,
    SessionService session,
    IOptions<MarketingOptions> marketingOpts) : Controller
{
    private MarketingOptions Marketing => marketingOpts.Value;

    [HttpGet("")]
    [HttpGet("home")]
    public IActionResult Index()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");

        ViewBag.Marketing = Marketing;
        return View(new LandingViewModel());
    }

    [HttpGet("precos")]
    public IActionResult Precos()
    {
        ViewBag.Marketing = Marketing;
        return View();
    }

    [HttpGet("app")]
    public IActionResult App()
    {
        ViewBag.Marketing = Marketing;
        return View();
    }

    [HttpGet("contato")]
    public IActionResult Contato(string? origem = null, string? assunto = null)
    {
        ViewBag.Marketing = Marketing;
        var vm = new ContatoViewModel
        {
            Mensagem = string.IsNullOrWhiteSpace(assunto) ? string.Empty : $"[{assunto}] ",
            UtmSource = origem
        };
        return View(vm);
    }

    [HttpPost("contato")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contato(ContatoViewModel vm)
    {
        ViewBag.Marketing = Marketing;
        if (!ModelState.IsValid) return View(vm);

        var result = await leadsApi.EnviarFaleConoscoAsync(vm);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty,
                result.ErrorMessage ?? "Nao deu pra enviar agora. Tenta novamente em alguns minutos.");
            return View(vm);
        }

        TempData["Toast"] = "success|Mensagem recebida! Retornamos em ate 1 dia util.";
        return RedirectToAction(nameof(Sucesso));
    }

    [HttpGet("sucesso")]
    public IActionResult Sucesso()
    {
        ViewBag.Marketing = Marketing;
        return View();
    }

    [HttpPost("newsletter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InscreverNewsletter(NewsletterViewModel vm)
    {
        if (!ModelState.IsValid)
            return Json(new { success = false, message = "E-mail invalido." });

        var result = await leadsApi.InscreverNewsletterAsync(vm);
        if (!result.Success)
            return Json(new { success = false, message = result.ErrorMessage ?? "Erro ao inscrever." });

        return Json(new { success = true, message = "Beleza, voce esta na lista." });
    }

    [HttpGet("termos")]
    public IActionResult TermosDeUso()
    {
        ViewBag.Marketing = Marketing;
        return View();
    }

    [HttpGet("privacidade")]
    public IActionResult PoliticaPrivacidade()
    {
        ViewBag.Marketing = Marketing;
        return View();
    }
}
