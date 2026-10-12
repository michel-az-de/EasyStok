using EasyStock.Application.UseCases.Atendimento.Programadas;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Mensagem programada ao cliente pelo console (S39, ADR-0051): agendar, listar e cancelar. O
/// disparo roda no processo da API (<c>MensagensProgramadasBackgroundService</c>).
/// </summary>
[SwaggerTag("Scheduled messages to customers")]
[ApiController]
[Route("api/atendimento/mensagens-programadas")]
[Authorize(Policy = "Operador")]
public class AtendimentoMensagensProgramadasController(
    AgendarMensagemProgramadaUseCase agendarUseCase,
    ListarMensagensProgramadasUseCase listarUseCase,
    CancelarMensagemProgramadaUseCase cancelarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Schedule a message to a customer",
        Description = "Recusa horário no passado, texto fora da janela do canal no horário do envio (use modelo no WhatsApp) e marketing sem consentimento no canal.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public async Task<IActionResult> Agendar([FromBody] AgendarMensagemProgramadaBody body, CancellationToken ct)
    {
        try
        {
            var modelo = body.Modelo is { } m ? new ModeloMensagem(m.Nome, m.Idioma ?? "pt_BR", m.Parametros ?? []) : null;
            var resultado = await agendarUseCase.ExecuteAsync(new AgendarMensagemProgramadaCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, body.ClienteId, body.ConversaId, body.Canal,
                body.Finalidade, body.Texto, modelo, body.AgendadaPara), ct);
            return DataOk(resultado);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "List scheduled messages (most recent first, up to 100)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] Guid? clienteId, [FromQuery] SituacaoMensagemProgramada? situacao, CancellationToken ct)
        => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, clienteId, situacao, ct));

    [SwaggerOperation(Summary = "Cancel a scheduled message", Description = "Só a agendada cancela; a que já está saindo não.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        try
        {
            return DataOk(await cancelarUseCase.ExecuteAsync(currentUser.EmpresaId, id, ct));
        }
        catch (MensagemProgramadaNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record ModeloMensagemBody(string Nome, string? Idioma, IReadOnlyList<string>? Parametros);

public sealed record AgendarMensagemProgramadaBody(
    Guid ClienteId,
    Guid? ConversaId,
    CanalConversa Canal,
    FinalidadeContato Finalidade,
    string? Texto,
    ModeloMensagemBody? Modelo,
    DateTime AgendadaPara);
