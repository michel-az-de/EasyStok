using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/tickets")]
[Authorize(Policy = "SuperAdmin")]
public class AdminTicketsController(EasyStockDbContext db, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? prioridade = null,
        [FromQuery] Guid? empresaId = null,
        [FromQuery] string? search = null)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        var query = db.AdminTickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var se))
            query = query.Where(t => t.Status == se);

        if (!string.IsNullOrWhiteSpace(prioridade) && Enum.TryParse<TicketPrioridade>(prioridade, out var pe))
            query = query.Where(t => t.Prioridade == pe);

        if (empresaId.HasValue)
            query = query.Where(t => t.EmpresaId == empresaId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Titulo.Contains(search));

        var total = await query.CountAsync();

        var tickets = await query
            .Include(t => t.Empresa)
            .Include(t => t.Atendente)
            .OrderByDescending(t => t.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Titulo,
                empresaNome = t.Empresa == null ? "" : t.Empresa.Nome,
                t.EmpresaId,
                status = t.Status.ToString(),
                categoria = t.Categoria.ToString(),
                prioridade = t.Prioridade.ToString(),
                atendenteNome = t.Atendente == null ? null : t.Atendente.Nome,
                t.AtendenteId,
                t.CriadoEm,
                t.AlteradoEm
            })
            .ToListAsync();

        return DataPaged(tickets, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTicket(Guid id)
    {
        var ticket = await db.AdminTickets
            .Include(t => t.Empresa)
            .Include(t => t.Atendente)
            .Include(t => t.Mensagens)
                .ThenInclude(m => m.Autor)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null) return DataNotFound("Ticket não encontrado.");

        return DataOk(new
        {
            ticket.Id,
            ticket.Titulo,
            ticket.Descricao,
            status = ticket.Status.ToString(),
            categoria = ticket.Categoria.ToString(),
            prioridade = ticket.Prioridade.ToString(),
            ticket.EmpresaId,
            empresaNome = ticket.Empresa?.Nome,
            ticket.AtendenteId,
            atendenteNome = ticket.Atendente?.Nome,
            ticket.CriadoEm,
            ticket.AlteradoEm,
            mensagens = ticket.Mensagens
                .OrderBy(m => m.CriadoEm)
                .Select(m => new
                {
                    m.Id,
                    m.Conteudo,
                    m.IsAdmin,
                    m.AutorId,
                    autorNome = m.Autor?.Nome,
                    m.CriadoEm
                })
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req)
    {
        var empresa = await db.Empresas.FindAsync(req.EmpresaId);
        if (empresa is null) return DataNotFound("Empresa não encontrada.");

        if (!Enum.TryParse<TicketCategoria>(req.Categoria, out var cat))
            return DataBadRequest("Categoria inválida.");
        if (!Enum.TryParse<TicketPrioridade>(req.Prioridade, out var pri))
            return DataBadRequest("Prioridade inválida.");

        var ticket = AdminTicket.Criar(req.EmpresaId, req.Titulo, req.Descricao, cat, pri);
        ticket.AtendenteId = currentUser.UsuarioId;
        db.AdminTickets.Add(ticket);
        await db.CommitAsync();

        return DataCreated($"/api/admin/tickets/{ticket.Id}", new { ticket.Id });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchTicket(Guid id, [FromBody] PatchTicketRequest req)
    {
        var ticket = await db.AdminTickets.FindAsync(id);
        if (ticket is null) return DataNotFound("Ticket não encontrado.");

        if (!string.IsNullOrWhiteSpace(req.Status) && Enum.TryParse<TicketStatus>(req.Status, out var se))
            ticket.Status = se;

        if (!string.IsNullOrWhiteSpace(req.Prioridade) && Enum.TryParse<TicketPrioridade>(req.Prioridade, out var pe))
            ticket.Prioridade = pe;

        if (req.AtendenteId.HasValue)
            ticket.AtendenteId = req.AtendenteId.Value;

        ticket.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();

        return DataOk(new { ticket.Id, status = ticket.Status.ToString() });
    }

    [HttpPost("{id:guid}/mensagens")]
    public async Task<IActionResult> AddMensagem(Guid id, [FromBody] AddMensagemRequest req)
    {
        var ticket = await db.AdminTickets.FindAsync(id);
        if (ticket is null) return DataNotFound("Ticket não encontrado.");

        var mensagem = AdminTicketMensagem.Criar(id, currentUser.UsuarioId, req.Conteudo, isAdmin: true);
        db.AdminTicketMensagens.Add(mensagem);

        if (ticket.Status == TicketStatus.Aberto)
        {
            ticket.Status = TicketStatus.EmAtendimento;
            ticket.AlteradoEm = DateTime.UtcNow;
        }

        await db.CommitAsync();
        return DataCreated($"/api/admin/tickets/{id}/mensagens/{mensagem.Id}", new { mensagem.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTicket(Guid id)
    {
        var ticket = await db.AdminTickets.FindAsync(id);
        if (ticket is null) return DataNotFound("Ticket não encontrado.");

        ticket.Status = TicketStatus.Fechado;
        ticket.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();

        return DataOk(new { id, status = "Fechado" });
    }
}

public record CreateTicketRequest(Guid EmpresaId, string Titulo, string Descricao, string Categoria, string Prioridade);
public record PatchTicketRequest(string? Status, string? Prioridade, Guid? AtendenteId);
public record AddMensagemRequest(string Conteudo);
