using EasyStock.Web.Models.ViewModels.Faq;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// FAQ publico — landing site (sem autenticacao). Espelha o endpoint
/// /api/faq da API. Layout proprio _LayoutSite.
/// </summary>
[AllowAnonymous]
[Route("faq")]
public sealed class FaqController(FaqApiService faqApi, ILogger<FaqController> log) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? termo = null, Guid? categoriaId = null, int page = 1)
    {
        var vm = new FaqIndexViewModel
        {
            Termo = termo,
            CategoriaId = categoriaId
        };

        var cats = await faqApi.ListarCategoriasAsync();
        if (cats.Success && cats.Data != null) vm.Categorias = cats.Data;

        var busca = await faqApi.BuscarAsync(termo, categoriaId, page <= 0 ? 1 : page, 10);
        if (busca.Success) vm.Resultado = busca.Data;
        else log.LogWarning("Falha ao buscar FAQ: {Erro}", busca.ErrorMessage);

        return View(vm);
    }

    [HttpGet("{categoriaSlug}/{itemSlug}")]
    public async Task<IActionResult> Detalhe(string categoriaSlug, string itemSlug)
    {
        var resp = await faqApi.ObterAsync(categoriaSlug, itemSlug);
        if (!resp.Success || resp.Data == null) return NotFound();

        var vm = new FaqDetalheViewModel { Item = resp.Data };
        var cats = await faqApi.ListarCategoriasAsync();
        if (cats.Success && cats.Data != null) vm.Categorias = cats.Data;

        return View(vm);
    }

    [HttpPost("feedback/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Feedback(Guid itemId, bool util, string? comentario, string? returnUrl = null)
    {
        await faqApi.EnviarFeedbackAsync(itemId, util, comentario);
        TempData["FaqFeedback"] = util ? "Obrigado pelo feedback!" : "Obrigado, vamos melhorar este item.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }
}
