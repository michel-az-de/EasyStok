using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Assistente;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;
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
    [HttpPost("cadastro")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> SugerirCadastro([FromBody] SugestaoCadastroBody body,
        [FromServices] SugerirCadastroConversaUseCase sugestao, CancellationToken ct = default)
    {
        try
        {
            return DataOk(await sugestao.ExecuteAsync(new(currentUser.EmpresaId, body?.ConversaId ?? Guid.Empty), ct));
        }
        catch (AssistenteDonaIndisponivelException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("ASSISTENTE_INDISPONIVEL", ex.Message, null, null)));
        }
        catch (ConversaNaoEncontradaException) { return DataNotFound("Conversa não encontrada."); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
    }

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

    /// <summary>S67 (#2534): o que o Assistente fez, turno a turno, do mais novo ao mais velho.</summary>
    [HttpGet("historico")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Historico([FromQuery] Guid? conversaId, [FromQuery] DesfechoTurnoAgente? resultado,
        [FromServices] ListarHistoricoAssistenteUseCase historico,
        [FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 20, CancellationToken ct = default)
    {
        try
        {
            return DataOk(await historico.ExecuteAsync(
                new ListarHistoricoAssistenteQuery(currentUser.EmpresaId, conversaId, resultado, pagina, tamanhoPagina), ct));
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
    }

    /// <summary>S67 (#2534): consumo do Assistente no mês de Brasília (sem ano e mês, o corrente).</summary>
    [HttpGet("uso")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Uso([FromQuery] int? ano, [FromQuery] int? mes,
        [FromServices] ObterUsoAssistenteUseCase uso, CancellationToken ct = default)
    {
        try
        {
            return DataOk(await uso.ExecuteAsync(new ObterUsoAssistenteQuery(currentUser.EmpresaId, ano, mes), ct));
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
    }

    [SwaggerOperation(Summary = "Ask the owner assistant (never sent to the customer)",
        Description = "Sem Anthropic:Enabled/Anthropic:ApiKey devolve 503.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost]
    [EnableRateLimiting("ai")]
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
public sealed record SugestaoCadastroBody(Guid ConversaId);
