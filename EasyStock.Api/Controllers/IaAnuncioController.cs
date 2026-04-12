using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.AnuncioIa;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[SwaggerTag("AI Listings / Anúncios IA")]
[ApiController]
[Route("api/ia")]
[Authorize]
public class IaAnuncioController(
    GerarAnuncioStreamingUseCase gerarUseCase,
    SalvarRascunhoAnuncioUseCase salvarUseCase,
    ListarAnunciosUseCase listarUseCase,
    ExcluirAnuncioUseCase excluirUseCase,
    ObterUsoIaUseCase obterUsoUseCase,
    IGeradorAutoPreenchimento geradorAutoPreenchimento,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Gera descricao de anuncio via SSE (streaming).
    /// Eventos: data: {"texto":"..."} ... data: [DONE]
    /// Erros SSE: event: erro\ndata: {"error": {...}}
    /// </summary>
    [SwaggerOperation(Summary = "Generate AI product listing (SSE stream)", Description = "Streams AI-generated product listing text via Server-Sent Events. Requires Operador role. Rate limited to 10 req/min.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("anuncio")]
    [Authorize(Policy = "Operador")]
    public async Task GerarAnuncioSse([FromBody] GerarAnuncioRequest request, CancellationToken ct)
    {
        var empresaId = ResolverEmpresaId(request.EmpresaId);

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            var command = new GerarAnuncioStreamingCommand(
                empresaId,
                request.ProdutoId,
                request.ProdutoVariacaoId,
                request.InstrucoesComplementares);

            await foreach (var chunk in gerarUseCase.ExecuteAsync(command, ct))
            {
                var payload = JsonSerializer.Serialize(new { texto = chunk });
                await Response.WriteAsync($"data: {payload}\n\n", Encoding.UTF8, ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (UseCaseValidationException ex)
        {
            var err = JsonSerializer.Serialize(new ApiErrorResponse(
                new ApiError("VALIDATION_ERROR", "Requisição inválida", ex.Message, null)));
            await Response.WriteAsync($"event: erro\ndata: {err}\n\n", Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>Salva um rascunho gerado.</summary>
    [SwaggerOperation(Summary = "Save generated listing")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("anuncio/salvar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> SalvarAnuncio([FromBody] SalvarAnuncioRequest request)
    {
        var empresaId = ResolverEmpresaId(request.EmpresaId);

        var resultado = await salvarUseCase.ExecuteAsync(new SalvarRascunhoAnuncioCommand(
            empresaId,
            request.ProdutoId,
            request.ProdutoVariacaoId,
            request.Titulo,
            request.Conteudo,
            request.InstrucoesUsadas,
            request.TokensConsumidos));

        return DataCreated($"/api/ia/anuncios/{resultado.Id}", resultado);
    }

    /// <summary>Lista anuncios salvos de um produto.</summary>
    [SwaggerOperation(Summary = "List saved listings for a product")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("anuncios/{prodId:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ListarAnuncios(Guid prodId, [FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        return DataOk(await listarUseCase.ExecuteAsync(new ListarAnunciosQuery(eid, prodId)));
    }

    /// <summary>Exclui um anuncio salvo.</summary>
    [SwaggerOperation(Summary = "Delete saved listing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("anuncios/{id:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ExcluirAnuncio(Guid id, [FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        await excluirUseCase.ExecuteAsync(new ExcluirAnuncioCommand(eid, id));
        return NoContent();
    }

    /// <summary>Consumo de IA do mes corrente para a empresa.</summary>
    [SwaggerOperation(Summary = "Get AI usage statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("uso")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ObterUso([FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        return DataOk(await obterUsoUseCase.ExecuteAsync(new ObterUsoIaQuery(eid)));
    }

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

    private Guid ResolverEmpresaId(Guid? solicitada) =>
        (solicitada.HasValue && solicitada != Guid.Empty) ? solicitada.Value : currentUser.EmpresaId;
}

public sealed record GerarAnuncioRequest(
    Guid? EmpresaId,
    Guid ProdutoId,
    Guid? ProdutoVariacaoId,
    string? InstrucoesComplementares);

public sealed record SalvarAnuncioRequest(
    Guid? EmpresaId,
    Guid ProdutoId,
    Guid? ProdutoVariacaoId,
    string Titulo,
    string Conteudo,
    string? InstrucoesUsadas,
    int TokensConsumidos);

public sealed record CompletarProdutoRequest(
    string NomeProduto,
    string? Categoria,
    string? Marca,
    string? Instrucoes);
