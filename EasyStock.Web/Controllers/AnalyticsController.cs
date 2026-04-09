using System.Text.Json;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Analytics;
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

        var (dashResult, reposResult, alertasResult) = (
            await svc.DashboardAsync(),
            await svc.ReposicaoAsync(),
            await svc.AlertasAsync()
        );

        if (dashResult.Success && dashResult.Data is JsonElement dash)
        {
            if (dash.TryGetProperty("receitaEstimadaPeriodo", out var r)) vm.ReceitaTotal = r.GetDecimal();
            if (dash.TryGetProperty("quantidadeTotalEmEstoque", out var te)) vm.TotalEstoque = te.GetInt32();
            if (dash.TryGetProperty("valorTotalEstoque", out var ve)) vm.ValorEstoque = ve.GetDecimal();

            // Derive projections from dashboard summary fields
            if (dash.TryGetProperty("mediaVendasDiaria", out var mvd))
            {
                var media = mvd.GetDecimal();
                vm.ProjUnidadesDia = media;
                vm.ProjUnidades7d = media * 7;
                vm.VelMedia = media;
            }
            if (dash.TryGetProperty("projecaoVendasPeriodo", out var pvp))
            {
                var proj = pvp.GetDecimal();
                vm.UnidadesVendidas = proj;
                vm.ProjUnidades30d = proj;
            }
            if (dash.TryGetProperty("receitaEstimadaPeriodo", out var rep)) vm.ProjReceita30d = rep.GetDecimal();
        }

        if (reposResult.Success)
            vm.ItensReposicaoUrgente = reposResult.Data ?? [];

        if (alertasResult.Success && alertasResult.Data is { } alertas)
        {
            vm.Alertas = alertas.Select(a => new AlertaItem(
                "validade",
                a.NomeProduto ?? a.CodigoInterno ?? "Produto",
                $"Vence em {a.DiasAteVencimento} dia(s) — {a.QuantidadeAtual} un. em risco",
                a.ItemEstoqueId
            )).ToList();
        }

        return View(vm);
    }

    [HttpGet("/analytics/movimentacoes")]
    public async Task<IActionResult> Movimentacoes(
        int page = 1, string? tipo = null, string? de = null, string? ate = null)
    {
        ViewBag.Title = "Movimentações";
        ViewBag.ActiveMenuItem = "Analytics";

        var result = await svc.MovimentacoesAsync(tipo, de, ate);
        var vm = new MovimentacoesViewModel
        {
            FiltroTipo = tipo,
            PeriodoInicio = de,
            PeriodoFim = ate
        };

        if (result.Success && result.Data is { } movs)
        {
            foreach (var m in movs)
            {
                vm.Itens.Add(new MovimentacaoItem
                {
                    Tipo = m.Tipo,
                    ProdutoNome = $"{m.TotalMovimentacoes} movimentação(ões)",
                    VariacaoNome = null,
                    Qty = m.QuantidadeTotal,
                    Valor = m.ValorTotal > 0 ? m.ValorTotal : null,
                    Data = new DateOnly(m.Ano, m.Mes, m.Dia)
                });
            }
        }

        return View(vm);
    }
}
