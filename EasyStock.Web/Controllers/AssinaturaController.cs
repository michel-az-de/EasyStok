using System.Text.Json;
using EasyStock.Web.Models.ViewModels.Assinatura;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AssinaturaController(AssinaturaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/assinatura")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Assinatura";
        ViewBag.ActiveMenuItem = "Assinatura";

        var vm = new AssinaturaViewModel();

        var planoResult = await svc.ObterAsync();
        if (planoResult.Success)
            vm.PlanoAtual = planoResult.Data;

        var planosResult = await svc.PlanosAsync();
        if (planosResult.Success && planosResult.Data is JsonElement planosEl
            && planosEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in planosEl.EnumerateArray())
            {
                var info = new PlanoInfo
                {
                    Nome = p.TryGetProperty("nome", out var n) ? n.GetString() ?? "" : "",
                    Preco = p.TryGetProperty("preco", out var pr) ? pr.GetDecimal() : 0m,
                    Recomendado = p.TryGetProperty("recomendado", out var rec) && rec.GetBoolean()
                };
                if (p.TryGetProperty("features", out var feats) && feats.ValueKind == JsonValueKind.Array)
                    info.Features = feats.EnumerateArray().Select(f => f.GetString() ?? "").ToList();
                vm.Planos.Add(info);
            }
        }

        var faturasResult = await svc.FaturasAsync();
        if (faturasResult.Success && faturasResult.Data is JsonElement faturasEl
            && faturasEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in faturasEl.EnumerateArray())
            {
                var dataStr = f.TryGetProperty("data", out var d) ? d.GetString() : null;
                vm.Faturas.Add(new FaturaInfo
                {
                    Data = dataStr != null ? DateOnly.Parse(dataStr) : DateOnly.FromDateTime(DateTime.Today),
                    Descricao = f.TryGetProperty("descricao", out var desc) ? desc.GetString() ?? "" : "",
                    Valor = f.TryGetProperty("valor", out var v) ? v.GetDecimal() : 0m,
                    Status = f.TryGetProperty("status", out var s) ? s.GetString() ?? "" : ""
                });
            }
        }

        return View(vm);
    }
}
