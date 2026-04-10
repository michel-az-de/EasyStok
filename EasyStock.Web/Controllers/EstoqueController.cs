using System.Text;
using EasyStock.Web.Models.ViewModels.Estoque;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EstoqueController(EstoqueService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/estoque")]
    public async Task<IActionResult> Index(int page = 1, string? search = null, string? status = null, string? categoria = null)
    {
        ViewBag.Title = "Estoque";
        ViewBag.ActiveMenuItem = "Estoque";

        var result = await svc.ListarAsync(page, status, categoria, search);
        if (HasError(result)) return View(new EstoqueListViewModel());

        var paged = result.Data!;
        var vm = new EstoqueListViewModel
        {
            Itens = paged.Data,
            Search = search,
            StatusFiltro = status,
            Categoria = categoria,
            Paginacao = new PaginationViewModel
            {
                Page = paged.Meta.Page,
                Pages = paged.Meta.Pages,
                Total = paged.Meta.Total,
                Limit = paged.Meta.Limit
            }
        };
        return View(vm);
    }

    [HttpGet("/estoque/exportar-csv")]
    public async Task<IActionResult> ExportarCsv()
    {
        var result = await svc.ExportarAsync();
        if (!result.Success) return BadRequest();

        var sb = new StringBuilder();
        sb.AppendLine("SKU,Produto,Variação,Quantidade,Status,Validade,Lote,Última Movimentação");
        foreach (var item in result.Data!.Data)
        {
            sb.AppendLine(string.Join(",",
                Csv(item.Sku),
                Csv(item.Produto?.Nome),
                Csv(item.Variacao?.Nome),
                item.Qty,
                Csv(item.Status),
                item.Validade?.ToString("yyyy-MM-dd") ?? "",
                Csv(item.Lote),
                item.LastMov.ToString("yyyy-MM-dd")));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"estoque-{DateTime.Now:yyyyMMdd}.csv");
    }

    private static string Csv(string? value) =>
        value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";


    [HttpGet("/estoque/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Item de Estoque";
        ViewBag.ActiveMenuItem = "Estoque";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var item = result.Data!;
        var vm = new EstoqueDetailViewModel
        {
            Item = item,
            Produto = item.Produto,
            Variacao = item.Variacao
        };
        return View(vm);
    }
}
