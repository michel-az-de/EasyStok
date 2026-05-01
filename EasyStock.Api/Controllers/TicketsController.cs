using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.TicketSuporte;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    [Authorize]
    public class TicketsController(
        AbrirTicketClienteUseCase abrirUseCase,
        ResponderTicketClienteUseCase responderUseCase,
        ListarMeusTicketsUseCase listarUseCase,
        ICurrentUserAccessor currentUser) : EasyStockControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> AbrirTicket([FromBody] AbrirTicketRequest req)
        {
            if (!currentUser.TemPermissao(Permissao.VisualizarTickets))
                return Forbid();

            var cmd = new AbrirTicketClienteCommand(req.Titulo, req.Descricao, req.Categoria);
            var result = await abrirUseCase.ExecuteAsync(cmd);

            return DataCreated($"api/tickets/{result.TicketId}", result);
        }

        [HttpGet]
        public async Task<IActionResult> ListarMeus(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? categoria = null)
        {
            if (!currentUser.TemPermissao(Permissao.VisualizarTickets))
                return Forbid();

            var cmd = new ListarMeusTicketsCommand(page, pageSize, status, categoria);
            var (dtos, total) = await listarUseCase.ExecuteAsync(cmd);

            var meta = new PagedMeta(total, (total + pageSize - 1) / pageSize, page, pageSize);
            return DataOk(new { items = dtos, meta });
        }

        [HttpPost("{id}/responder")]
        public async Task<IActionResult> Responder(Guid id, [FromBody] ResponderRequest req)
        {
            if (!currentUser.TemPermissao(Permissao.ResponderTickets))
                return Forbid();

            var cmd = new ResponderTicketClienteCommand(id, req.Resposta);
            await responderUseCase.ExecuteAsync(cmd);

            return NoContent();
        }
    }
}
