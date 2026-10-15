using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// SSE do auto-preenchimento de produto no cadastro. A tela de anuncios com IA
/// saiu na poda P03 (#1116); a rota antiga foi mantida para o formulario de produto.
/// </summary>
public class AutoPreenchimentoController(
    AutoPreenchimentoService autoPreenchimentoSvc,
    SessionService session) : BaseController(session)
{
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

        var (success, stream, error) = await autoPreenchimentoSvc.CompletarProdutoStreamAsync(
            nome, categoria, marca, instrucoes);

        if (!success || stream is null)
        {
            // Issue 808: erro como JSON estruturado (antes texto cru quebrava o JSON.parse
            // do consumidor e o spinner sumia sem feedback).
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new { error })}\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        using var s = stream;
        using var reader = new StreamReader(s);

        try
        {
            while (await reader.ReadLineAsync(HttpContext.RequestAborted) is { } line)
            {
                await Response.WriteAsync(line + "\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException) { /* cliente desconectou */ }
    }
}
