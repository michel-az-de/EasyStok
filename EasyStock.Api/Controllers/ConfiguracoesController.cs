using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.ConfiguracoesLoja;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/configuracoes")]
[Authorize(Policy = "Gerente")]
public class ConfiguracoesController(
    ObterConfiguracaoLojaUseCase obterUseCase,
    AtualizarConfiguracaoLojaUseCase atualizarUseCase,
    ResetarConfiguracaoLojaUseCase resetarUseCase,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid empresaId, [FromQuery] Guid lojaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var result = await obterUseCase.ExecuteAsync(new ObterConfiguracaoLojaQuery(empresaId, lojaId));
        return Ok(result);
    }

    [HttpPatch]
    public async Task<IActionResult> Patch([FromBody] AtualizarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var result = await atualizarUseCase.ExecuteAsync(command);
        return Ok(result);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var result = await resetarUseCase.ExecuteAsync(command);
        return Ok(result);
    }
}
