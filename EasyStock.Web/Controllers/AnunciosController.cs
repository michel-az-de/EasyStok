using EasyStock.Web.Models.ViewModels.Anuncios;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AnunciosController(
    ProdutosService produtosSvc,
    AnunciosService anunciosSvc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/anuncios")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Anúncios IA";
        ViewBag.ActiveMenuItem = "Anuncios";

        var result = await produtosSvc.ListarAsync(1, 50);
        var vm = new AnunciosViewModel
        {
            Produtos = result.Success ? result.Data!.Data : []
        };

        return View(vm);
    }

    [HttpGet("/anuncios/salvos")]
    public async Task<IActionResult> ListarSalvos(string produtoId)
    {
        if (string.IsNullOrWhiteSpace(produtoId)) return Json(Array.Empty<object>());
        var result = await anunciosSvc.ListarSalvosAsync(produtoId);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data!.Select(a => new
        {
            id = a.Id,
            titulo = a.Titulo,
            conteudo = a.Conteudo,
            criadoEm = a.CriadoEm.ToString("dd/MM/yyyy HH:mm")
        }));
    }

    [HttpPost("/anuncios/salvar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(
        string produtoId, string? variacaoId, string titulo, string conteudo, string? instrucoes)
    {
        var result = await anunciosSvc.SalvarAnuncioAsync(produtoId, variacaoId, titulo, conteudo, instrucoes);
        if (!result.Success)
            return Json(new { ok = false, erro = result.ErrorMessage ?? "Erro ao salvar." });
        return Json(new { ok = true });
    }

    [HttpPost("/anuncios/{id}/deletar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await anunciosSvc.DeletarAnuncioAsync(id);
        if (!result.Success)
            return Json(new { ok = false, erro = result.ErrorMessage ?? "Erro ao deletar." });
        return Json(new { ok = true });
    }

    // GET is intentional — SSE streams never need anti-forgery
    [HttpGet("/anuncios/completar-produto")]
    public async Task CompletarProduto(
        string? nome, string? categoria, string? marca, string? instrucoes)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        if (string.IsNullOrWhiteSpace(nome))
        {
            await Response.WriteAsync("data: {\"texto\":\"Informe o nome do produto.\"}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        var (success, stream, error) = await anunciosSvc.CompletarProdutoStreamAsync(
            nome, categoria, marca, instrucoes);

        if (!success || stream is null)
        {
            await Response.WriteAsync($"data: {error}\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        using var s = stream;
        using var reader = new StreamReader(s);

        while (!reader.EndOfStream && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            await Response.WriteAsync(line + "\n");
            await Response.Body.FlushAsync();
        }
    }

    // GET is intentional — SSE streams never need anti-forgery
    [HttpGet("/anuncios/gerar")]
    public async Task Gerar(
        string? produtoId, string? canal, string? tom,
        string? foco, string? contexto, string? varId)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var (success, stream, error) = await anunciosSvc.GerarStreamAsync(
            produtoId ?? "",
            canal ?? "ML",
            tom ?? "profissional",
            foco ?? "beneficios",
            varId,
            contexto);

        if (!success || stream is null)
        {
            await Response.WriteAsync($"data: {error}\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        using var s = stream;
        using var reader = new StreamReader(s);

        while (!reader.EndOfStream && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            await Response.WriteAsync(line + "\n");
            await Response.Body.FlushAsync();
        }
    }
}
