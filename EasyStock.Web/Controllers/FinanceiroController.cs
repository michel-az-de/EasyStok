using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class FinanceiroController(FinanceiroService svc, SessionService session) : BaseController(session)
{
    // ── Criação rápida: Categoria ─────────────────────────────────────────────

    [HttpPost("/financeiro/categorias/criar-rapido")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarCategoriaRapido([FromBody] CriarCategoriaRapidoBody req)
    {
        if (string.IsNullOrWhiteSpace(req.Nome))
            return new JsonResult(new { message = "Nome é obrigatório." }) { StatusCode = 400 };

        var tipo = (req.Tipo ?? "").Trim();
        if (tipo != "Despesa" && tipo != "Receita")
            return new JsonResult(new { message = "Tipo inválido. Use 'Despesa' ou 'Receita'." }) { StatusCode = 400 };

        var result = await svc.CriarCategoriaAsync(req.Nome.Trim(), tipo, null, null, null);
        if (!result.Success || result.Data is null)
            return new JsonResult(new { message = result.ErrorMessage ?? "Erro ao criar categoria." }) { StatusCode = 500 };

        return Json(new { id = result.Data.Id, label = result.Data.Nome });
    }

    // ── Criação rápida: Centro de Custo ───────────────────────────────────────

    [HttpPost("/financeiro/centros-custo/criar-rapido")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarCentroCustoRapido([FromBody] CriarCentroCustoRapidoBody req)
    {
        if (string.IsNullOrWhiteSpace(req.Nome))
            return new JsonResult(new { message = "Nome é obrigatório." }) { StatusCode = 400 };

        var nomeTrimmed = req.Nome.Trim();
        var codigoBase  = GerarCodigoCc(nomeTrimmed);

        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            var codigo = tentativa == 1
                ? codigoBase
                : $"{codigoBase[..Math.Min(codigoBase.Length, 17)]}-{tentativa}";

            var result = await svc.CriarCentroCustoAsync(codigo, nomeTrimmed, null, null);
            if (result.Success && result.Data is not null)
                return Json(new { id = result.Data.Id, label = $"{result.Data.Codigo} - {result.Data.Nome}" });

            // Só repete se for conflito de código; qualquer outro erro retorna imediatamente.
            if (result.HttpStatus != 409 && result.ErrorCode != "CONFLICT")
                return new JsonResult(new { message = result.ErrorMessage ?? "Erro ao criar centro de custo." }) { StatusCode = 500 };
        }

        return new JsonResult(new { message = "Não foi possível gerar um código único. Tente novamente." }) { StatusCode = 500 };
    }

    private static string GerarCodigoCc(string nome)
    {
        // Remove diacríticos, mantém só [A-Z0-9], uppercase, trunca em 20 chars.
        var normalized = nome.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        var s = System.Text.RegularExpressions.Regex
            .Replace(sb.ToString().ToUpperInvariant(), @"[^A-Z0-9]+", "-")
            .Trim('-');
        if (s.Length > 20) s = s[..20].TrimEnd('-');
        return string.IsNullOrEmpty(s) ? "CC" : s;
    }

    // ── Views ─────────────────────────────────────────────────────────────────

    [HttpGet("/financeiro")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Financeiro";
        ViewBag.ActiveMenuItem = "Financeiro";
        var result = await svc.ObterDashboardAsync();
        DashboardFinanceiroApi dashboard = result.Success && result.Data is not null
            ? result.Data
            : new DashboardFinanceiroApi();
        if (!result.Success && result.ErrorMessage is not null) Toast("error", result.ErrorMessage);
        return View(dashboard);
    }

    [HttpGet("/financeiro/fluxo-caixa")]
    public async Task<IActionResult> FluxoCaixa(string periodicidade = "Mensal", DateTime? inicio = null, DateTime? fim = null)
    {
        ViewBag.Title = "Fluxo de Caixa";
        ViewBag.ActiveMenuItem = "Financeiro";
        ViewBag.Periodicidade = periodicidade;

        var iniDef = inicio ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var fimDef = fim ?? iniDef.AddMonths(6).AddDays(-1);
        ViewBag.Inicio = iniDef.ToString("yyyy-MM-dd");
        ViewBag.Fim = fimDef.ToString("yyyy-MM-dd");

        var result = await svc.ObterFluxoCaixaAsync(periodicidade, iniDef, fimDef);
        var buckets = result.Success && result.Data is not null ? result.Data : new List<FluxoBucketApi>();
        if (!result.Success && result.ErrorMessage is not null) Toast("error", result.ErrorMessage);
        return View(buckets);
    }
}

// ── DTOs de body para criação rápida ─────────────────────────────────────────
public record CriarCategoriaRapidoBody(string? Nome, string? Tipo);
public record CriarCentroCustoRapidoBody(string? Nome);
