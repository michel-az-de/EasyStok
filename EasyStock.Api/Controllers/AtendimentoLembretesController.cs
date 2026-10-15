using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Lembretes internos da dona pelo console (S43, ADR-0051): o sininho. Os automáticos (pagamento sem
/// baixa, cliente sem resposta) nascem do avaliador que roda no processo da API
/// (<c>AvaliadorLembretesBackgroundService</c>). Nada sai para o cliente.
/// </summary>
[SwaggerTag("Owner reminders")]
[ApiController]
[Route("api/atendimento/lembretes")]
[Authorize(Policy = "Operador")]
public class AtendimentoLembretesController(
    CriarLembreteUseCase criarUseCase,
    ListarLembretesUseCase listarUseCase,
    ConcluirLembreteUseCase concluirUseCase,
    MarcarLembretesVistosUseCase vistosUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List reminders (open by default, up to 100)",
        Description = "Traz os lembretes do usuário e os da equipe toda. todos=true inclui os de outros atendentes.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool todos, [FromQuery] bool incluirConcluidos, CancellationToken ct)
        => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, currentUser.UsuarioId, todos, incluirConcluidos, ct));

    [SwaggerOperation(Summary = "Create a manual reminder", Description = "Sem venceEm, vence agora e o aviso sai na próxima rodada do avaliador.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarLembreteBody body, CancellationToken ct)
    {
        try
        {
            return DataOk(await criarUseCase.ExecuteAsync(new CriarLembreteCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, body.Texto, body.VenceEm, body.ParaUsuarioId,
                body.ConversaId, body.PedidoId), ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Complete a reminder", Description = "Idempotente.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/concluir")]
    public async Task<IActionResult> Concluir(Guid id, CancellationToken ct)
    {
        try
        {
            return DataOk(await concluirUseCase.ExecuteAsync(currentUser.EmpresaId, currentUser.UsuarioId, id, ct));
        }
        catch (LembreteNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Mark visible due reminders as seen", Description = "Devolve quantos foram marcados.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpPost("vistos")]
    public async Task<IActionResult> MarcarVistos(CancellationToken ct)
        => DataOk(new { marcados = await vistosUseCase.ExecuteAsync(currentUser.EmpresaId, currentUser.UsuarioId, ct) });
}

public sealed record CriarLembreteBody(string Texto, DateTime? VenceEm, Guid? ParaUsuarioId, Guid? ConversaId, Guid? PedidoId);
