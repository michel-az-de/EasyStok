using EasyStock.Web.Models.ViewModels.Site;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Landing publica do EasyStok. Todas as actions sao anonimas, com layout
/// proprio (_LayoutSite) — separado do app autenticado. Usuarios ja logados
/// sao redirecionados pro dashboard pra nao verem pitch de venda novamente.
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
    LeadsApiService leadsApi,
    SessionService session) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        // Usuario ja autenticado vai direto pro dashboard — landing e pitch de venda.
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");

        return View(new LandingViewModel());
    }

    [HttpGet("precos")]
    public IActionResult Precos() => View();

    [HttpGet("app")]
    public IActionResult App() => View();

    [HttpGet("contato")]
    public IActionResult Contato(string? origem = null, string? assunto = null)
    {
        // Sanitiza assunto para evitar payload arbitrario via querystring no textarea.
        // Texto livre limitado a 80 chars + caracteres seguros.
        var assuntoSanitizado = string.IsNullOrWhiteSpace(assunto)
            ? null
            : SanitizarAssunto(assunto);

        var vm = new ContatoViewModel
        {
            Mensagem = string.IsNullOrEmpty(assuntoSanitizado) ? string.Empty : $"[{assuntoSanitizado}] ",
            UtmSource = string.IsNullOrWhiteSpace(origem) ? null : SanitizarAssunto(origem)
        };
        return View(vm);
    }

    [HttpPost("contato")]
    public async Task<IActionResult> Contato(ContatoViewModel vm)
    {
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
    public IActionResult Sucesso() => View();

    /// <summary>
    /// POST AJAX da newsletter (footer). Resposta JSON pra o JS exibir toast inline
    /// sem reload. Anti-spam adicional do API (rate-limit por IP) ainda se aplica
    /// porque o ApiClient encaminha o IP via X-Forwarded-For (TODO: P1).
    /// </summary>
    [HttpPost("newsletter")]
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
    public IActionResult TermosDeUso() => View();

    [HttpGet("privacidade")]
    public IActionResult PoliticaPrivacidade() => View();

    /// <summary>
    /// Sanitiza texto vindo de querystring para uso em textarea/input pre-preenchido.
    /// Limita tamanho, remove control chars e neutraliza tentativas obvias de
    /// injecao (apesar do Razor escape proteger no render, melhor evitar payload
    /// chegar na server-side validation).
    /// </summary>
    private static string SanitizarAssunto(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length > 80) trimmed = trimmed[..80];
        var clean = new string(trimmed.Where(c => !char.IsControl(c) && c != '<' && c != '>' && c != '"').ToArray());
        return clean;
    }
}
