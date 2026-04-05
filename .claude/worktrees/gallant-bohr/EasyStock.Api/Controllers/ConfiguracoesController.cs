using EasyStock.Api.Http;
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
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid empresaId, [FromQuery] Guid lojaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await obterUseCase.ExecuteAsync(new ObterConfiguracaoLojaQuery(empresaId, lojaId)));
    }

    [HttpPatch]
    public async Task<IActionResult> Patch([FromBody] AtualizarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        return DataOk(await atualizarUseCase.ExecuteAsync(command));
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetarConfiguracaoLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        return DataOk(await resetarUseCase.ExecuteAsync(command));
    }
}
