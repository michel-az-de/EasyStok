using EasyStock.Application.UseCases.ConfiguracoesLoja;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Store Configuration / Configurações")]
[ApiController]
[Route("api/configuracoes")]
[Authorize(Policy = "Gerente")]
public class ConfiguracoesController(
    ObterConfiguracaoLojaUseCase obterUseCase,
    AtualizarConfiguracaoLojaUseCase atualizarUseCase,
    ResetarConfiguracaoLojaUseCase resetarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get store configuration (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid empresaId, [FromQuery] Guid lojaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await obterUseCase.ExecuteAsync(new ObterConfiguracaoLojaQuery(empresaId, lojaId)));
    }

    [SwaggerOperation(Summary = "Update store configuration (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPatch]
    public async Task<IActionResult> Patch([FromBody] AtualizarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        return DataOk(await atualizarUseCase.ExecuteAsync(command));
    }

    [SwaggerOperation(Summary = "Reset store configuration to defaults (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        return DataOk(await resetarUseCase.ExecuteAsync(command));
    }
}
