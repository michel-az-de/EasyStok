using EasyStock.Api.Http;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.TicketSuporte;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    [Authorize]
    public class TicketsController(
        AbrirTicketClienteUseCase abrirUseCase,
        ResponderTicketClienteUseCase responderUseCase,
        ListarMeusTicketsUseCase listarUseCase,
        IClienteTicketRepository ticketRepo,
        HelpdeskAnexoService anexoService,
        ICurrentUserAccessor currentUser) : EasyStockControllerBase
    {
        [HttpPost]
        [EnableRateLimiting("tickets-post")]
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
        [EnableRateLimiting("tickets-post")]
        public async Task<IActionResult> Responder(Guid id, [FromBody] ResponderRequest req)
        {
            if (!currentUser.TemPermissao(Permissao.ResponderTickets))
                return Forbid();

            var cmd = new ResponderTicketClienteCommand(id, req.Resposta);
            await responderUseCase.ExecuteAsync(cmd);

            return NoContent();
        }

        [HttpPost("{id:guid}/anexos")]
        [RequestSizeLimit(15 * 1024 * 1024)]
        public async Task<IActionResult> AnexarCliente(Guid id, IFormFile file)
        {
            if (!currentUser.TemPermissao(Permissao.ResponderTickets))
                return Forbid();
            if (file is null || file.Length == 0) return DataBadRequest("Arquivo obrigatorio.");

            // Garantir que o ticket pertence ao cliente atual antes de aceitar upload.
            var ticket = await ticketRepo.GetByIdAsync(currentUser.EmpresaId, id, clienteId: currentUser.UsuarioId);
            if (ticket is null) return DataNotFound("Ticket nao encontrado.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            try
            {
                var anexo = await anexoService.AnexarAsync(new AnexarArquivoCommand(
                    id, MensagemId: null, file.FileName, file.ContentType, ms.ToArray(), IsAdmin: false));
                return DataCreated($"/api/tickets/{id}/anexos/{anexo.Id}", new { anexo.Id, anexo.Url });
            }
            catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
        }
    }
}
