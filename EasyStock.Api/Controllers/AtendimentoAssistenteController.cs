using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Assistente da dona (S47): pergunta livre com contexto opcional da conversa. Somente leitura; a
/// resposta volta só para o console e nunca é enviada ao cliente.
/// </summary>
[SwaggerTag("Attendance owner assistant (console)")]
[ApiController]
[Route("api/atendimento/assistente")]
[Authorize(Policy = "Operador")]
public class AtendimentoAssistenteController(
    AssistenteDonaUseCase assistenteUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpPost("ficha")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> MontarFicha([FromBody] MontagemFichaBody body,
        [FromServices] MontagemFichaUseCase montagem, CancellationToken ct = default)
    {
        try
        {
            return DataOk(await montagem.ExecuteAsync(new MontagemFichaCommand(currentUser.EmpresaId,
                body?.ConversaId ?? Guid.Empty, body?.Orientacao), ct));
        }
        catch (MontagemFichaIndisponivelException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("FICHA_IA_INDISPONIVEL", ex.Message, null, null)));
        }
        catch (ConversaNaoEncontradaException) { return DataNotFound("Conversa não encontrada."); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
    }

    [SwaggerOperation(Summary = "Ask the owner assistant (never sent to the customer)",
        Description = "Sem Anthropic:Enabled/Anthropic:ApiKey devolve 503.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost]
    public async Task<IActionResult> Perguntar([FromBody] PerguntarAssistenteBody body, CancellationToken ct = default)
    {
        try
        {
            return DataOk(await assistenteUseCase.ExecuteAsync(
                new PerguntarAssistenteDonaCommand(currentUser.EmpresaId, body?.Pergunta ?? string.Empty, body?.ConversaId), ct));
        }
        catch (AssistenteDonaIndisponivelException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("ASSISTENTE_INDISPONIVEL", ex.Message, null, null)));
        }
        catch (ConversaNaoEncontradaException)
        {
            return DataNotFound("Conversa não encontrada.");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}

public sealed record PerguntarAssistenteBody(string Pergunta, Guid? ConversaId);
public sealed record MontagemFichaBody(Guid ConversaId, string? Orientacao = null);
