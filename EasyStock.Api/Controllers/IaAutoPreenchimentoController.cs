using EasyStock.Application.Ports.Output.Ai;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Auto-preenchimento de produto novo via IA. Sobreviveu a poda P03 (#1116), que
/// removeu os anuncios com IA; o cadastro de produto ainda usa este endpoint.
/// </summary>
[SwaggerTag("AI Auto-fill / Auto-preenchimento de produto")]
[ApiController]
[Route("api/ia")]
[Authorize]
public class IaAutoPreenchimentoController(
    IGeradorAutoPreenchimento geradorAutoPreenchimento) : EasyStockControllerBase
{
    /// <summary>
    /// Preenche automaticamente os campos de um produto novo via SSE (streaming).
    /// Não exige produtoId — recebe nome, categoria, marca e instrucoes.
    /// Eventos: data: {"texto":"..."} ... data: [DONE]
    /// </summary>
    [SwaggerOperation(Summary = "Auto-fill new product fields via AI (SSE stream)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("completar-produto")]
    [Authorize(Policy = "Operador")]
    public async Task CompletarProdutoSse([FromBody] CompletarProdutoRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in geradorAutoPreenchimento.GerarDescricaoProdutoStreamAsync(
                request.NomeProduto,
                request.Categoria,
                request.Marca,
                request.Instrucoes,
                ct))
            {
                var payload = JsonSerializer.Serialize(new { texto = chunk });
                await Response.WriteAsync($"data: {payload}\n\n", Encoding.UTF8, ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            var err = JsonSerializer.Serialize(new { error = ex.Message });
            await Response.WriteAsync($"event: erro\ndata: {err}\n\n", Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

public sealed record CompletarProdutoRequest(
    string NomeProduto,
    string? Categoria,
    string? Marca,
    string? Instrucoes);
