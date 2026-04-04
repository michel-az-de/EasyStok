using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/planos")]
public class PlanoController(IPlanoRepository planoRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var planos = await planoRepository.GetAtivosAsync();
        return Ok(planos);
    }
}
