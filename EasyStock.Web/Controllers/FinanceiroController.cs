using System.Globalization;
using EasyStock.Web.Helpers;
using EasyStock.Web.Models.ViewModels.Financeiro;
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

        // A view consome FinanceiroIndexViewModel (KPIs escalares + deltas/series derivados).
        // Os deltas alimentam os <es-stat-card> (seta de tendencia + sparkline); calculados no
        // Web a partir do fluxo de caixa diario e degradam pra null sem quebrar a pagina.
        var deltas = await CalcularDeltasAsync();
        return View(new FinanceiroIndexViewModel { Dashboard = dashboard, Deltas = deltas });
    }

    [HttpGet("/financeiro/fluxo-caixa")]
    public async Task<IActionResult> FluxoCaixa(string periodicidade = "Mensal", DateTime? inicio = null, DateTime? fim = null)
    {
        ViewBag.Title = "Fluxo de Caixa";
        ViewBag.ActiveMenuItem = "Financeiro";
        ViewBag.Periodicidade = periodicidade;

        // Servidor roda UTC (Render); perto da meia-noite BRT o DateTime.UtcNow pula de mês.
        // Usar BrazilTime.Today() pro início do mês padrão (mesmo padrão do CaixaController).
        var hojeBr = BrazilTime.Today();
        var iniDef = inicio ?? new DateTime(hojeBr.Year, hojeBr.Month, 1);
        var fimDef = fim ?? iniDef.AddMonths(6).AddDays(-1);
        ViewBag.Inicio = iniDef.ToString("yyyy-MM-dd");
        ViewBag.Fim = fimDef.ToString("yyyy-MM-dd");

        var result = await svc.ObterFluxoCaixaAsync(periodicidade, iniDef, fimDef);
        var buckets = result.Success && result.Data is not null ? result.Data : new List<FluxoBucketApi>();
        if (!result.Success && result.ErrorMessage is not null) Toast("error", result.ErrorMessage);
        return View(buckets);
    }

    // ── Deltas + sparklines do dashboard (divisor de aguas) ───────────────────
    // Calculados 100% no Web a partir do fluxo de caixa diario (ObterFluxoCaixaAsync),
    // sem migrar o DTO de dominio. Janela movel 30d (decisao 8 do plano de DS):
    //   Receber/Pagar 30d (o KPI olha pra frente): proximos 30d vs 30d anteriores.
    //   Saldo do mes (realizado): ultimos 30d vs 30d anteriores.
    // Sem base de comparacao (periodo anterior == 0) o delta vira null e a view nao
    // desenha seta/sparkline — pagina continua funcionando.
    private async Task<DashboardDeltasApi> CalcularDeltasAsync()
    {
        var deltas = new DashboardDeltasApi();
        var hoje = BrazilTime.Today();
        var inicio = hoje.AddDays(-59).ToDateTime(TimeOnly.MinValue);
        var fim = hoje.AddDays(29).ToDateTime(TimeOnly.MinValue);

        var fluxo = await svc.ObterFluxoCaixaAsync("Diario", inicio, fim);
        if (!fluxo.Success || fluxo.Data is null || fluxo.Data.Count == 0) return deltas;
        var buckets = fluxo.Data.OrderBy(b => b.InicioBucket).ToList();

        (deltas.ReceberDelta, deltas.ReceberTrend, deltas.ReceberSerie) =
            CalcularMetrica(buckets, hoje, b => b.PrevistoReceber, olhaPraFrente: true);
        (deltas.PagarDelta, deltas.PagarTrend, deltas.PagarSerie) =
            CalcularMetrica(buckets, hoje, b => b.PrevistoPagar, olhaPraFrente: true);
        (deltas.SaldoDelta, deltas.SaldoTrend, deltas.SaldoSerie) =
            CalcularMetrica(buckets, hoje, b => b.RealizadoReceber - b.RealizadoPagar, olhaPraFrente: false);

        return deltas;
    }

    private static (string? delta, string trend, string? serie) CalcularMetrica(
        List<FluxoBucketApi> buckets, DateOnly hoje, Func<FluxoBucketApi, decimal> metrica, bool olhaPraFrente)
    {
        DateOnly atualIni = olhaPraFrente ? hoje : hoje.AddDays(-29);
        DateOnly atualFim = olhaPraFrente ? hoje.AddDays(29) : hoje;
        DateOnly antIni = olhaPraFrente ? hoje.AddDays(-30) : hoje.AddDays(-59);
        DateOnly antFim = olhaPraFrente ? hoje.AddDays(-1) : hoje.AddDays(-30);

        static bool Entre(FluxoBucketApi b, DateOnly a, DateOnly z)
        {
            var d = DateOnly.FromDateTime(b.InicioBucket);
            return d >= a && d <= z;
        }

        var atual = buckets.Where(b => Entre(b, atualIni, atualFim)).ToList();
        var somaAtual = atual.Sum(metrica);
        var somaAnterior = buckets.Where(b => Entre(b, antIni, antFim)).Sum(metrica);

        // Sparkline: serie diaria (CSV InvariantCulture) da janela atual.
        string? serie = atual.Count > 0
            ? string.Join(",", atual.Select(b => metrica(b).ToString(CultureInfo.InvariantCulture)))
            : null;

        if (somaAnterior == 0m) return (null, "flat", serie);

        var pct = (somaAtual - somaAnterior) / Math.Abs(somaAnterior) * 100m;
        var pctAbsStr = Math.Abs(pct).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"));
        if (pctAbsStr == "0") return ("0%", "flat", serie);

        var trend = pct > 0 ? "up" : "down";
        var delta = $"{(pct > 0 ? "+" : "-")}{pctAbsStr}%";
        return (delta, trend, serie);
    }
}

// ── DTOs de body para criação rápida ─────────────────────────────────────────
public record CriarCategoriaRapidoBody(string? Nome, string? Tipo);
public record CriarCentroCustoRapidoBody(string? Nome);
