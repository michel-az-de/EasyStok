using System.Text.Json;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Analytics;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AnalyticsController(AnalyticsService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/analytics")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Analytics";
        ViewBag.ActiveMenuItem = "Analytics";

        var vm = new AnalyticsViewModel();

        var (dashResult, projResult, reposResult, alertasResult) = (
            await svc.DashboardAsync(),
            await svc.ProjecoesAsync(),
            await svc.ReposicaoAsync(),
            await svc.AlertasAsync()
        );

        if (dashResult.Success && dashResult.Data is JsonElement dash)
        {
            if (dash.TryGetProperty("receitaTotal", out var r)) vm.ReceitaTotal = r.GetDecimal();
            if (dash.TryGetProperty("totalEstoque", out var te)) vm.TotalEstoque = te.GetInt32();
            if (dash.TryGetProperty("valorEstoque", out var ve)) vm.ValorEstoque = ve.GetDecimal();
            if (dash.TryGetProperty("unidadesVendidas", out var uv)) vm.UnidadesVendidas = uv.GetInt32();
        }

        if (projResult.Success && projResult.Data is JsonElement proj)
        {
            if (proj.TryGetProperty("dia", out var d)) vm.ProjUnidadesDia = d.GetDecimal();
            if (proj.TryGetProperty("sete", out var s7)) vm.ProjUnidades7d = s7.GetDecimal();
            if (proj.TryGetProperty("trinta", out var s30)) vm.ProjUnidades30d = s30.GetDecimal();
            if (proj.TryGetProperty("receita30d", out var rc)) vm.ProjReceita30d = rc.GetDecimal();
        }

        if (reposResult.Success)
            vm.ItensReposicaoUrgente = reposResult.Data ?? [];

        if (alertasResult.Success && alertasResult.Data is JsonElement alertas)
        {
            if (alertas.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in alertas.EnumerateArray())
                {
                    var tipo = a.TryGetProperty("tipo", out var t) ? t.GetString() ?? "" : "";
                    var titulo = a.TryGetProperty("titulo", out var tt) ? tt.GetString() ?? "" : "";
                    var msg = a.TryGetProperty("mensagem", out var m) ? m.GetString() ?? "" : "";
                    var refId = a.TryGetProperty("referenciaId", out var ri) ? ri.GetString() : null;
                    vm.Alertas.Add(new AlertaItem(tipo, titulo, msg, refId));
                }
            }
        }

        return View(vm);
    }

    [HttpGet("/analytics/movimentacoes")]
    public async Task<IActionResult> Movimentacoes(
        int page = 1, string? tipo = null, string? de = null, string? ate = null)
    {
        ViewBag.Title = "Movimentações";
        ViewBag.ActiveMenuItem = "Analytics";

        var result = await svc.MovimentacoesAsync(page, tipo, de, ate);
        var vm = new MovimentacoesViewModel
        {
            FiltroTipo = tipo,
            PeriodoInicio = de,
            PeriodoFim = ate
        };

        if (result.Success)
        {
            var paged = result.Data!;
            vm.Paginacao = new PaginationViewModel
            {
                Page = paged.Meta.Page,
                Pages = paged.Meta.Pages,
                Total = paged.Meta.Total,
                Limit = paged.Meta.Limit
            };

            foreach (var item in paged.Data)
            {
                if (item is JsonElement el)
                {
                    vm.Itens.Add(new MovimentacaoItem
                    {
                        Tipo = el.TryGetProperty("tipo", out var t) ? t.GetString() ?? "" : "",
                        ProdutoNome = el.TryGetProperty("produtoNome", out var pn) ? pn.GetString() ?? "" : "",
                        VariacaoNome = el.TryGetProperty("variacaoNome", out var vn) ? vn.GetString() : null,
                        Qty = el.TryGetProperty("qty", out var q) ? q.GetInt32() : 0,
                        Valor = el.TryGetProperty("valor", out var v) && v.ValueKind != JsonValueKind.Null
                            ? v.GetDecimal() : null,
                        Data = el.TryGetProperty("data", out var d)
                            ? DateOnly.Parse(d.GetString()!) : DateOnly.FromDateTime(DateTime.Today)
                    });
                }
            }
        }

        return View(vm);
    }
}
