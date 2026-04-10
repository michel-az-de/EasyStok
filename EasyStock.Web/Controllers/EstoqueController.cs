using System.Text;
using EasyStock.Web.Models.ViewModels.Estoque;
using EasyStock.Web.Models.ViewModels.Saidas;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EstoqueController(EstoqueService svc, SaidasService saidasSvc, SessionService session) : BaseController(session)
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

    [HttpPost("/estoque/saida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickSaida([FromBody] QuickSaidaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EstoqueId) || req.Qty < 1)
            return BadRequest(new { success = false, errorMessage = "Dados inválidos. Verifique o item e a quantidade." });

        var itemResult = await svc.ObterAsync(req.EstoqueId);
        if (!itemResult.Success || itemResult.Data is null)
            return BadRequest(new { success = false, errorMessage = "Item de estoque não encontrado." });

        var item = itemResult.Data;

        if (req.Qty > item.Qty)
            return BadRequest(new { success = false, errorMessage = $"Estoque insuficiente. Disponível: {item.Qty}." });

        if (!DateOnly.TryParse(req.Data, out var data))
            data = DateOnly.FromDateTime(DateTime.Today);

        var saidaVm = new SaidaFormViewModel
        {
            ProdutoId = item.ProdutoId,
            VarId = item.VarId,
            Natureza = req.Natureza ?? "venda",
            Qty = req.Qty,
            Valor = req.Valor,
            DtVenda = data
        };

        var result = await saidasSvc.CriarAsync(saidaVm);
        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao registrar saída." });

        return Ok(new { success = true });
    }

    [HttpGet("/estoque/produto-detalhe/{id}")]
    public async Task<IActionResult> ProdutoDetalhe(string id)
    {
        var result = await svc.ObterProdutoDetalheAsync(id);
        if (!result.Success || result.Data is null)
            return NotFound(new { error = "Produto não encontrado." });

        var p = result.Data;
        return Json(new
        {
            id = p.ProdutoId,
            nome = p.Nome,
            sku = p.SkuBase,
            estoqueTotal = p.QuantidadeTotalEstoque,
            custoReferencia = p.CustoReferencia,
            precoReferencia = p.PrecoReferencia,
            controlaValidade = p.ControlaValidade,
            margemEstimada = p.MargemEstimada,
            variacoes = p.Variacoes
                .Where(v => v.Ativa)
                .Select(v => new { id = v.VariacaoId, nome = v.Nome, quantidadeEmEstoque = v.QuantidadeEmEstoque })
        });
    }

    [HttpGet("/estoque/itens-por-produto/{produtoId}")]
    public async Task<IActionResult> ItensPorProduto(string produtoId)
    {
        var result = await svc.ObterItensPorProdutoAsync(produtoId);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data);
    }
}
