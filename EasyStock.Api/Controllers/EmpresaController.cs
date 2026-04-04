using EasyStock.Application.UseCases.RegistrarEmpresa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/empresas")]
public class EmpresaController(RegistrarEmpresaUseCase registrarUseCase) : ControllerBase
{
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEmpresaCommand command)
    {
        var resultado = await registrarUseCase.ExecuteAsync(command);
        return Created("", resultado);
    }
}
