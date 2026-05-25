using EasyStock.Api.Http;
using EasyStock.Api.Services.Helpdesk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/empresas")]
[Authorize(Policy = "SuperAdmin")]
public class AdminEmpresaPreviewController(HelpdeskClienteService clienteService) : EasyStockControllerBase
{
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        try
        {
            var dados = await clienteService.PreviewMascaradoAsync(id);
            return DataOk(dados);
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
    }

    [HttpPost("{id:guid}/preview/revelar")]
    public async Task<IActionResult> Revelar(Guid id, [FromBody] RevelarRequest req)
    {
        try
        {
            var dados = await clienteService.RevelarAsync(new RevelarClienteCommand(id, req.Motivo, req.TicketIdContexto));
            return DataOk(dados);
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
    }

    public sealed record RevelarRequest(string Motivo, Guid? TicketIdContexto);
}
