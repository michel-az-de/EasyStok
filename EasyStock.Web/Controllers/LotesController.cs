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

public class AtualizarPesoWebRequest
{
    public int PesoG { get; set; }
}

public class LotesController(
    LotesService svc,
    SessionService session,
    ILogger<LotesController> log) : BaseController(session)
{
    [HttpGet("/lotes")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Lotes";
        ViewBag.ActiveMenuItem = "Lotes";
        var vm = new LotesListViewModel { Search = search, FiltroStatus = status };

        // /lotes vinha quebrando em 500 esporadicamente. O ApiClient captura erros
        // de rede/parse, mas qualquer outra exceção (NRE em DTO mal-formado, etc.)
        // escapava direto para o ErrorController. Aqui mantemos a página viva com
        // lista vazia + toast e logamos o stack pra triagem.
        try
        {
            // C2 (R10): consulta pendentes em paralelo a list. Status "pendente_peso"
            // e filtro client-side — chama endpoint dedicado.
            var pendentesTask = svc.ListarPendentesPesoAsync();

            if (status == "pendente_peso")
            {
                vm.Items = new List<EasyStock.Web.Models.Api.Lote>();
            }
            else
            {
                var result = await svc.ListarAsync(status, search);
                if (result.Success && result.Data is not null) vm.Items = result.Data;
                else if (!result.Success)
                    log.LogWarning("Lotes.Listar falhou: {Code} {Message} (HTTP {Http} CID {Cid})",
                        result.ErrorCode, result.ErrorMessage, result.HttpStatus, result.CorrelationId);
            }

            var pendentes = await pendentesTask;
            if (pendentes.Success && pendentes.Data is not null)
            {
                vm.PendentesPesoCount = pendentes.Data.Count;
                if (status == "pendente_peso") vm.PendentesPeso = pendentes.Data;
            }
            else if (!pendentes.Success)
                log.LogWarning("Lotes.ListarPendentesPeso falhou: {Code} {Message} (HTTP {Http} CID {Cid})",
                    pendentes.ErrorCode, pendentes.ErrorMessage, pendentes.HttpStatus, pendentes.CorrelationId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha inesperada carregando /lotes (search={Search}, status={Status})", search, status);
            Toast("warning", "Não foi possível carregar todos os lotes agora. Tente novamente em instantes.");
        }

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
            return StatusCode(result.HttpStatus > 0 ? result.HttpStatus : 400, new
            {
                success = false,
                error = new
                {
                    code = result.ErrorCode ?? "API_ERROR",
                    message = result.ErrorMessage ?? "Erro ao criar lote."
                }
            });
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
        Toast("success", $"Lote finalizado. {result.Data?.TotalUnidades} etiqueta(s) prontas. <a href='/lotes/{id}/imprimir'>Imprimir agora →</a>");
        return RedirectToAction(nameof(Detail), new { id });
    }

    /// <summary>
    /// C2 backfill — PATCH peso de item. Bloqueado se lote ja finalizado (R3).
    /// </summary>
    [HttpPatch("/lotes/{loteId}/itens/{itemId}/peso")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarPeso(string loteId, string itemId, [FromBody] AtualizarPesoWebRequest req)
    {
        if (req.PesoG <= 0)
            return BadRequest(new { success = false, error = new { code = "INVALID_PESO", message = "Peso deve ser maior que zero." } });

        var result = await svc.AtualizarPesoItemAsync(loteId, itemId, req.PesoG);
        if (!result.Success)
            return StatusCode(result.HttpStatus > 0 ? result.HttpStatus : 400, new
            {
                success = false,
                error = new
                {
                    code = result.ErrorCode ?? "API_ERROR",
                    message = result.ErrorMessage ?? "Erro ao atualizar peso."
                }
            });
        return Ok(new { success = true });
    }

    [HttpGet("/lotes/{id}/imprimir")]
    public async Task<IActionResult> Imprimir(string id)
    {
        ViewBag.Title     = "Imprimir etiquetas";
        ViewBag.LoteId    = id;
        ViewBag.EmpresaId = Session.GetEmpresaId();
        return View();
    }
}
