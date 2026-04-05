using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AnuncioIa;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/ia")]
[Authorize]
public class IaAnuncioController(
    GerarAnuncioStreamingUseCase gerarUseCase,
    SalvarRascunhoAnuncioUseCase salvarUseCase,
    ListarAnunciosUseCase listarUseCase,
    ExcluirAnuncioUseCase excluirUseCase,
    ObterUsoIaUseCase obterUsoUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Gera descricao de anuncio via SSE (streaming).
    /// Eventos: data: {"texto":"..."} ... data: [DONE]
    /// Erros SSE: event: erro\ndata: {"error": {...}}
    /// </summary>
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
                new ApiError("VALIDATION_ERROR", "Requisicao invalida", ex.Message, null)));
            await Response.WriteAsync($"event: erro\ndata: {err}\n\n", Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>Salva um rascunho gerado.</summary>
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
    [HttpGet("anuncios/{prodId:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ListarAnuncios(Guid prodId, [FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        return DataOk(await listarUseCase.ExecuteAsync(new ListarAnunciosQuery(eid, prodId)));
    }

    /// <summary>Exclui um anuncio salvo.</summary>
    [HttpDelete("anuncios/{id:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ExcluirAnuncio(Guid id, [FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        await excluirUseCase.ExecuteAsync(new ExcluirAnuncioCommand(eid, id));
        return NoContent();
    }

    /// <summary>Consumo de IA do mes corrente para a empresa.</summary>
    [HttpGet("uso")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ObterUso([FromQuery] Guid? empresaId)
    {
        var eid = ResolverEmpresaId(empresaId);
        return DataOk(await obterUsoUseCase.ExecuteAsync(new ObterUsoIaQuery(eid)));
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
