using System.Globalization;
using System.Text;
using EasyStock.Web.Models.ViewModels.Entradas;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EntradasController(EntradasService svc, EstoqueService estoqueSvc, SessionService session) : BaseController(session)
{
    [HttpGet("/entradas/nova")]
    public IActionResult Nova()
    {
        ViewBag.Title = "Nova Entrada";
        ViewBag.ActiveMenuItem = "Entradas";
        return View(new EntradaFormViewModel());
    }

    [HttpPost("/entradas/nova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(EntradaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Nova Entrada";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Nova", vm);
        }

        if (!ValidarProdutoId(vm))
        {
            ViewBag.Title = "Nova Entrada";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Nova", vm);
        }

        var result = await svc.CriarEntradaAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Nova Entrada";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Nova", vm);
        }

        Toast("success", "Entrada registrada com sucesso!");
        return RedirectToAction("Index", "Estoque");
    }

    [HttpGet("/entradas/reposicao")]
    public async Task<IActionResult> Reposicao(string? itemId = null)
    {
        ViewBag.Title = "Reposição Rápida";
        ViewBag.ActiveMenuItem = "Entradas";

        var vm = new ReposicaoFormViewModel();

        if (!string.IsNullOrEmpty(itemId))
        {
            var result = await estoqueSvc.ObterAsync(itemId);
            if (result.Success && result.Data is not null)
            {
                var item = result.Data;
                vm.ItemEstoqueId = item.Id;
                vm.ProdutoId = item.ProdutoId;
                vm.ProdutoNome = item.Produto?.Nome;
                vm.VariacaoNome = item.Variacao?.Nome;
                vm.EstoqueAtual = item.Qty;
                vm.Custo = item.CustoUnitario?.Valor;
                vm.Preco = item.PrecoVendaSugerido?.Valor;
            }
        }

        return View(vm);
    }

    [HttpPost("/entradas/reposicao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarReposicao(ReposicaoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Reposição Rápida";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Reposicao", vm);
        }

        if (string.IsNullOrEmpty(vm.ItemEstoqueId) || !Guid.TryParse(vm.ItemEstoqueId, out _))
        {
            ModelState.AddModelError("ItemEstoqueId", "Selecione um item de estoque válido.");
            ViewBag.Title = "Reposição Rápida";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Reposicao", vm);
        }

        var result = await svc.ReposicaoAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Reposição Rápida";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Reposicao", vm);
        }

        Toast("success", "Reposição registrada com sucesso!");
        return RedirectToAction("Index", "Estoque");
    }

    [HttpGet("/entradas/historico")]
    public async Task<IActionResult> Historico(int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        ViewBag.Title = "Histórico de Entradas";
        ViewBag.ActiveMenuItem = "Entradas";

        var result = await svc.HistoricoAsync(page, tipo, periodoInicio, periodoFim);
        if (HasError(result)) return View(new EntradasHistoricoViewModel());

        var paged = result.Data!;
        var vm = new EntradasHistoricoViewModel
        {
            Entradas = paged.Data,
            Tipo = tipo,
            PeriodoInicio = periodoInicio,
            PeriodoFim = periodoFim,
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

    [HttpGet("/entradas/exportar-csv")]
    public async Task<IActionResult> ExportarCsv(string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        var result = await svc.ExportarAsync(tipo, periodoInicio, periodoFim);
        if (!result.Success) return BadRequest();

        var sb = new StringBuilder();
        sb.AppendLine("Data,Produto,Variação,Quantidade,Custo Unitário,Total,Natureza,Documento,Descrição");
        foreach (var m in result.Data!.Data)
        {
            sb.AppendLine(string.Join(",",
                m.Data.ToString("yyyy-MM-dd"),
                Csv(m.Produto?.Nome),
                Csv(m.ProdutoVariacao?.Nome),
                m.Qty,
                m.Custo?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                m.ValorTotal?.Valor.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                Csv(m.Natureza),
                Csv(m.DocumentoReferencia),
                Csv(m.Descricao)));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"entradas-{DateTime.Now:yyyyMMdd}.csv");
    }

    private bool ValidarProdutoId(EntradaFormViewModel vm)
    {
        if (!string.IsNullOrEmpty(vm.ProdutoId) && Guid.TryParse(vm.ProdutoId, out _))
            return true;
        ModelState.AddModelError("ProdutoId", "Selecione um produto válido.");
        return false;
    }

    private static string Csv(string? value) =>
        value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
}
