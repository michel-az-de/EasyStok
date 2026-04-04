using EasyStock.Application.UseCases.ListarPlanos;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/planos")]
public class PlanoController(ListarPlanosUseCase listarPlanosUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await listarPlanosUseCase.ExecuteAsync());
}
