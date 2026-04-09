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
            Produtos = result.Success ? result.Data! : []
        };

        return View(vm);
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
