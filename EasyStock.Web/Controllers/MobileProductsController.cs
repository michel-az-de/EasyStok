using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /produtos-mobile — revisão de produtos criados no app (Onda 2).
/// Operador pode aprovar (manter mobile-only) ou linkar com Produto ERP.
/// </summary>
public class MobileProductsController(
    MobileProductsService svc,
    ProdutosService produtosErp,
    SessionService session) : BaseController(session)
{
    [HttpGet("/produtos-mobile")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Produtos do mobile";
        ViewBag.ActiveMenuItem = "ProdutosMobile";

        // Lista todos os produtos custom — pendentes + já revisados
        // (pra operador ver histórico).
        var result = await svc.ListarTodosCustomAsync();
        if (HasError(result)) return View(new List<MobileProductApi>());

        // Pra modal de "linkar com Produto ERP", carrega lista (paginada)
        // dos produtos do ERP. Limit 100 — se houver mais, modal mostra
        // search com debounce no futuro.
        var erpResult = await produtosErp.ListarAsync(page: 1, limit: 100);
        ViewBag.ProdutosErp = erpResult.Success && erpResult.Data?.Data != null
            ? erpResult.Data.Data
            : new List<EasyStock.Web.Models.Api.ProdutoResumo>();

        return View(result.Data ?? []);
    }

    [HttpPost("/produtos-mobile/{id}/aprovar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprovar(string id)
    {
        var result = await svc.AprovarAsync(id);
        Toast(result.Success ? "success" : "error",
            result.Success ? "Produto aprovado." : (result.ErrorMessage ?? "Falha ao aprovar."));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/produtos-mobile/{id}/linkar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Linkar(string id, Guid erpProductId)
    {
        if (erpProductId == Guid.Empty)
        {
            Toast("error", "Selecione um produto ERP.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.LinkarAsync(id, erpProductId);
        Toast(result.Success ? "success" : "error",
            result.Success ? "Produto linkado ao ERP." : (result.ErrorMessage ?? "Falha ao linkar."));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/produtos-mobile/{id}/desfazer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desfazer(string id)
    {
        var result = await svc.DesfazerAsync(id);
        Toast(result.Success ? "success" : "error",
            result.Success ? "Aprovação desfeita." : (result.ErrorMessage ?? "Falha ao desfazer."));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Aba "Divergências" — lista e permite reconciliar (Onda 2 p2).</summary>
    [HttpGet("/produtos-mobile/divergencias")]
    public async Task<IActionResult> Divergencias()
    {
        ViewBag.Title = "Divergências de estoque";
        ViewBag.ActiveMenuItem = "ProdutosMobile";

        var result = await svc.ListarDivergenciasAsync();
        if (HasError(result)) return View(new List<StockDivergenceApi>());
        return View(result.Data ?? []);
    }

    [HttpPost("/produtos-mobile/{id}/reconciliar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconciliar(string id)
    {
        var result = await svc.ReconciliarAsync(id);
        Toast(result.Success ? "success" : "error",
            result.Success ? "Estoque reconciliado." : (result.ErrorMessage ?? "Falha ao reconciliar."));
        return RedirectToAction(nameof(Divergencias));
    }
}
