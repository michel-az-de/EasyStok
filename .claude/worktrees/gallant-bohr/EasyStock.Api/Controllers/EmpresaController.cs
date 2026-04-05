using EasyStock.Api.Http;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/empresas")]
public class EmpresaController(RegistrarEmpresaUseCase registrarUseCase) : EasyStockControllerBase
{
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEmpresaCommand command)
    {
        var resultado = await registrarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/empresas/{resultado.EmpresaId}", resultado);
    }
}
