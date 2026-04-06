using EasyStock.Api.Http;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Company / Empresa")]
[ApiController]
[Route("api/empresas")]
public class EmpresaController(RegistrarEmpresaUseCase registrarUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Register a new company", Description = "Creates company account with initial admin user and default plan. Does not require authentication.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEmpresaCommand command)
    {
        var resultado = await registrarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/empresas/{resultado.EmpresaId}", resultado);
    }
}
