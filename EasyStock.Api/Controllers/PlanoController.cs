using EasyStock.Api.Http;
using EasyStock.Application.UseCases.ListarPlanos;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/planos")]
public class PlanoController(ListarPlanosUseCase listarPlanosUseCase) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => DataOk(await listarPlanosUseCase.ExecuteAsync());
}
