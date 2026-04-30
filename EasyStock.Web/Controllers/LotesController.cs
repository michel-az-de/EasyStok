using EasyStock.Web.Models.ViewModels.Lotes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CriarLoteWebRequest
{
    public string? CodigoCustom { get; set; }
    public string? OperadorNome { get; set; }
    public string? Observacoes { get; set; }
    public List<CriarLoteItemInput>? Itens { get; set; }
}

public class LotesController(LotesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/lotes")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Lotes";
        ViewBag.ActiveMenuItem = "Lotes";
        var vm = new LotesListViewModel { Search = search, FiltroStatus = status };

        var result = await svc.ListarAsync(status, search);
        if (result.Success && result.Data is not null) vm.Items = result.Data;

        return View(vm);
    }

    [HttpGet("/lotes/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Lote";
        ViewBag.ActiveMenuItem = "Lotes";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new LoteDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/lotes/json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarJson([FromBody] CriarLoteWebRequest req)
    {
        var result = await svc.CriarAsync(req.CodigoCustom, req.OperadorNome, req.Observacoes, req.Itens);
        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao criar lote." });
        return Ok(new { success = true, id = result.Data?.Id });
    }

    [HttpPost("/lotes/{id}/itens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string id, string nome, int quantidade,
        Guid? produtoId, string? emoji, string? unidade, int? pesoG, int? validadeDias)
    {
        var result = await svc.AdicionarItemAsync(id, nome, quantidade, produtoId, emoji, unidade, pesoG, validadeDias);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Item adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/lotes/{id}/itens/{itemId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(string id, string itemId)
    {
        var result = await svc.RemoverItemAsync(id, itemId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Item removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/lotes/{id}/finalizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalizar(string id)
    {
        var result = await svc.FinalizarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", $"Lote finalizado. {result.Data?.TotalUnidades} etiqueta(s) geradas.");
        return RedirectToAction(nameof(Detail), new { id });
    }
}
