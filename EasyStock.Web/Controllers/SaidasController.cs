using System.Text;
using EasyStock.Web.Constants;
using EasyStock.Web.Models.ViewModels.Saidas;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class SaidasController(SaidasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/saidas")]
    [HttpGet("/saidas/nova")]
    public IActionResult Nova(string? produtoId = null)
    {
        ViewBag.Title = "Nova Saída";
        ViewBag.ActiveMenuItem = "Saidas";
        if (!string.IsNullOrEmpty(produtoId))
            ViewBag.PreSelProdutoId = produtoId;
        return View(new SaidaFormViewModel());
    }

    [HttpPost("/saidas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(SaidaFormViewModel vm)
    {
        var isFetch = Request.Headers[CustomHeaders.FetchRequest] == CustomHeaders.FetchRequestEnabled;

        if (!ModelState.IsValid)
        {
            if (isFetch)
            {
                var msg = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    ?? "Dados inválidos.";
                return BadRequest(new { erro = msg });
            }
            ViewBag.Title = "Nova Saída";
            ViewBag.ActiveMenuItem = "Saidas";
            return View("Nova", vm);
        }

        var result = await svc.CriarAsync(vm);
        if (!result.Success)
        {
            var erro = result.ErrorMessage ?? "Erro ao registrar saída.";
            if (isFetch)
                return BadRequest(new { erro });
            Toast("error", erro);
            ViewBag.Title = "Nova Saída";
            ViewBag.ActiveMenuItem = "Saidas";
            return View("Nova", vm);
        }

        Toast("success", "Saída registrada com sucesso!");
        return RedirectToAction(nameof(Historico));
    }

    [HttpGet("/saidas/historico")]
    public async Task<IActionResult> Historico(
        int page = 1, string? natureza = null, string? de = null, string? ate = null)
    {
        ViewBag.Title = "Histórico de Saídas";
        ViewBag.ActiveMenuItem = "Saidas";

        var result = await svc.ListarAsync(page, natureza, de, ate);
        var kpisResult = await svc.ObterKpisAsync(natureza, de, ate);

        var vm = new SaidasHistoricoViewModel
        {
            FiltroNatureza = natureza,
            PeriodoInicio = de,
            PeriodoFim = ate
        };

        if (!result.Success)
        {
            HasError(result);
            return View(vm);
        }

        var paged = result.Data!;
        vm.Itens = paged.Data;
        vm.TotalRegistros = paged.Meta.Total;
        vm.Paginacao = new Models.ViewModels.Shared.PaginationViewModel
        {
            Page = paged.Meta.Page,
            Pages = paged.Meta.Pages,
            Total = paged.Meta.Total,
            Limit = paged.Meta.Limit
        };

        // Server-side KPIs (across all records, not just current page)
        if (kpisResult.Success && kpisResult.Data is not null)
        {
            vm.TotalUnidades = kpisResult.Data.TotalUnidades;
            vm.ReceitaTotal = kpisResult.Data.ReceitaTotal;
            vm.TotalVendas = kpisResult.Data.TotalVendas;
            vm.TotalPerdas = kpisResult.Data.TotalPerdas;
        }

        return View(vm);
    }

    [HttpGet("/saidas/exportar-csv")]
    public async Task<IActionResult> ExportarCsv(string? periodoInicio = null, string? periodoFim = null)
    {
        var result = await svc.ExportarAsync(periodoInicio, periodoFim);
        if (!result.Success) return BadRequest();

        var sb = new StringBuilder();
        sb.AppendLine("Data,Produto,Variação,Quantidade,Valor Unitário,Total,Natureza,Documento,Descrição");
        foreach (var m in result.Data!.Data)
        {
            sb.AppendLine(string.Join(",",
                m.Data.ToString("yyyy-MM-dd"),
                Csv(m.Produto?.Nome),
                Csv(m.ProdutoVariacao?.Nome),
                m.Qty,
                m.ValorUnitario?.Valor.ToString("F2") ?? "",
                m.ValorTotal?.Valor.ToString("F2") ?? "",
                Csv(m.Natureza),
                Csv(m.DocumentoReferencia),
                Csv(m.Descricao)));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"saidas-{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpPost("/saidas/{id}/estornar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estornar(string id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 3)
        {
            Toast("error", "Informe um motivo do estorno (mínimo 3 caracteres).");
            return RedirectToAction(nameof(Historico));
        }

        var result = await svc.EstornarAsync(id, motivo.Trim());
        if (HasError(result)) return RedirectToAction(nameof(Historico));

        Toast("success", "Saída estornada com sucesso! O estoque foi restaurado.");
        return RedirectToAction(nameof(Historico));
    }

    private static string Csv(string? value) =>
        value is null ? "" : $"\"{value.Replace("\"", "\"\"").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")}\"";
}
