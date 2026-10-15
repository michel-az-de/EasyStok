using EasyStock.Application.UseCases.Atendimento.Inbox;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Atendentes da empresa (S41): quem o console pode escolher ao transferir uma conversa.
/// </summary>
[SwaggerTag("Attendants")]
[ApiController]
[Route("api/atendimento/atendentes")]
[Authorize(Policy = "Operador")]
public class AtendimentoAtendentesController(
    ListarAtendentesUseCase listarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List active users who can attend conversations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct = default)
        => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, ct));
}
