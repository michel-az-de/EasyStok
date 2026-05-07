using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/tickets")]
[Authorize(Policy = "SuperAdmin")]
public class AdminTicketsController(
    EasyStockDbContext db,
    AdminAuditService audit,
    HelpdeskTicketService ticketService,
    HelpdeskAnexoService anexoService,
    HelpdeskBugFixService bugFixService) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? prioridade = null,
        [FromQuery] string? nivel = null,
        [FromQuery] string? slaStatus = null,
        [FromQuery] string? categoria = null,
        [FromQuery] Guid? empresaId = null,
        [FromQuery] Guid? atendenteId = null,
        [FromQuery] string? search = null)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        var query = db.AdminTickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var se))
            query = query.Where(t => t.Status == se);

        if (!string.IsNullOrWhiteSpace(prioridade) && Enum.TryParse<TicketPrioridade>(prioridade, out var pe))
            query = query.Where(t => t.Prioridade == pe);

        if (!string.IsNullOrWhiteSpace(nivel) && Enum.TryParse<NivelAtendimento>(nivel, out var ne))
            query = query.Where(t => t.Nivel == ne);

        if (!string.IsNullOrWhiteSpace(categoria) && Enum.TryParse<TicketCategoria>(categoria, out var ce))
            query = query.Where(t => t.Categoria == ce);

        if (empresaId.HasValue)
            query = query.Where(t => t.EmpresaId == empresaId.Value);

        if (atendenteId.HasValue)
            query = query.Where(t => t.AtendenteId == atendenteId.Value);

        if (!string.IsNullOrWhiteSpace(slaStatus))
        {
            query = slaStatus.ToLowerInvariant() switch
            {
                "violado" => query.Where(t => t.SlaRespostaViolado || t.SlaResolucaoViolado),
                "ok" => query.Where(t => !t.SlaRespostaViolado && !t.SlaResolucaoViolado),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Titulo.Contains(search));

        var total = await query.CountAsync();

        // Ordena por urgencia: violado > prazo proximo > criado
        var tickets = await query
            .Include(t => t.Empresa)
            .Include(t => t.Atendente)
            .OrderByDescending(t => t.SlaRespostaViolado || t.SlaResolucaoViolado)
            .ThenBy(t => t.PrazoResposta ?? DateTime.MaxValue)
            .ThenByDescending(t => t.CriadoEm)
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
                nivel = t.Nivel.ToString(),
                atendenteNome = t.Atendente == null ? null : t.Atendente.Nome,
                t.AtendenteId,
                t.PrazoResposta,
                t.PrazoResolucao,
                t.SlaRespostaViolado,
                t.SlaResolucaoViolado,
                t.CriadoEm,
                t.AlteradoEm,
                // N+1 fix: usa navigation .Mensagens em vez de db.AdminTicketMensagens
                // separado. EF Core traduz como subquery escalar (UMA query, nao
                // 1+pageSize). Anteriormente: 21 queries por pagina de 20 tickets.
                mensagensNaoLidas = t.Mensagens.Count(m => !m.IsAdmin && !m.LidoPeloAdmin)
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
            .Include(t => t.OrigemTicket)
            .Include(t => t.MetaTecnico)
            .Include(t => t.Mensagens)
                .ThenInclude(m => m.Autor)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null) return DataNotFound("Ticket não encontrado.");

        await db.AdminTicketMensagens
            .Where(m => m.TicketId == id && !m.IsAdmin && !m.LidoPeloAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LidoPeloAdmin, true));

        var anexos = await db.TicketAnexos
            .AsNoTracking()
            .Where(a => a.TicketId == id)
            .OrderBy(a => a.CriadoEm)
            .Select(a => new { a.Id, a.MensagemId, a.NomeArquivo, a.ContentType, a.TamanhoBytes, a.Url, a.IsAdmin, a.CriadoEm })
            .ToListAsync();

        var historico = await db.TicketHistoricos
            .AsNoTracking()
            .Where(h => h.TicketId == id)
            .OrderBy(h => h.CriadoEm)
            .Select(h => new { h.Id, h.AutorId, acao = h.Acao.ToString(), h.ValorAntes, h.ValorDepois, h.MetadadosJson, h.CriadoEm })
            .ToListAsync();

        return DataOk(new
        {
            ticket.Id,
            ticket.Titulo,
            ticket.Descricao,
            status = ticket.Status.ToString(),
            categoria = ticket.Categoria.ToString(),
            prioridade = ticket.Prioridade.ToString(),
            nivel = ticket.Nivel.ToString(),
            ticket.EmpresaId,
            empresaNome = ticket.Empresa?.Nome,
            ticket.AtendenteId,
            atendenteNome = ticket.Atendente?.Nome,
            ticket.PrazoResposta,
            ticket.PrazoResolucao,
            ticket.PrimeiraRespostaEm,
            ticket.ResolvidoEm,
            ticket.SlaRespostaViolado,
            ticket.SlaResolucaoViolado,
            ticket.OrigemTicketId,
            origemTicketTitulo = ticket.OrigemTicket?.Titulo,
            metaTecnico = ticket.MetaTecnico is null ? null : new
            {
                ticket.MetaTecnico.SeveridadeTecnica,
                ticket.MetaTecnico.ComponenteAfetado,
                ticket.MetaTecnico.FixVersion,
                ticket.MetaTecnico.ResolvidoEm
            },
            ticket.CriadoEm,
            ticket.AlteradoEm,
            mensagens = ticket.Mensagens
                .OrderBy(m => m.CriadoEm)
                .Select(m => new
                {
                    m.Id,
                    m.Conteudo,
                    m.IsAdmin,
                    m.Interno,
                    m.AutorId,
                    autorNome = m.Autor?.Nome,
                    m.CriadoEm
                }),
            anexos,
            historico
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req)
    {
        if (!Enum.TryParse<TicketCategoria>(req.Categoria, out var cat))
            return DataBadRequest("Categoria inválida.");
        if (!Enum.TryParse<TicketPrioridade>(req.Prioridade, out var pri))
            return DataBadRequest("Prioridade inválida.");
        var nivel = NivelAtendimento.N1;
        if (!string.IsNullOrWhiteSpace(req.Nivel) && Enum.TryParse<NivelAtendimento>(req.Nivel, out var n))
            nivel = n;

        try
        {
            var ticket = await ticketService.AbrirAsync(new AbrirAdminTicketCommand(
                req.EmpresaId, req.Titulo, req.Descricao, cat, pri, nivel, AnexoIds: null));
            await audit.LogAsync("TicketCriado", $"Titulo={req.Titulo}, EmpresaId={req.EmpresaId}", req.EmpresaId);
            return DataCreated($"/api/admin/tickets/{ticket.Id}", new { ticket.Id });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchTicket(Guid id, [FromBody] PatchTicketRequest req)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(req.Status) && Enum.TryParse<TicketStatus>(req.Status, out var se))
                await ticketService.AlterarStatusAsync(new AlterarStatusTicketCommand(id, se));
            if (!string.IsNullOrWhiteSpace(req.Prioridade) && Enum.TryParse<TicketPrioridade>(req.Prioridade, out var pe))
                await ticketService.AlterarPrioridadeAsync(new AlterarPrioridadeTicketCommand(id, pe));
            if (req.AtendenteId.HasValue)
                await ticketService.AtribuirAsync(new AtribuirTicketCommand(id, req.AtendenteId.Value));
            return DataOk(new { id });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/mensagens")]
    public async Task<IActionResult> AddMensagem(Guid id, [FromBody] AddMensagemRequest req)
    {
        try
        {
            var msg = await ticketService.ResponderAsync(new ResponderAdminTicketCommand(
                id, req.Conteudo, req.Interno, req.AnexoIds));
            await audit.LogAsync("TicketRespondido", $"TicketId={id}, Interno={req.Interno}", null);
            return DataCreated($"/api/admin/tickets/{id}/mensagens/{msg.Id}", new { msg.Id });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/assumir")]
    public async Task<IActionResult> Assumir(Guid id)
    {
        try
        {
            await ticketService.AssumirAsync(new AssumirTicketCommand(id));
            return DataOk(new { id });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
    }

    [HttpPost("{id:guid}/encaminhar")]
    public async Task<IActionResult> Encaminhar(Guid id, [FromBody] EncaminharRequest req)
    {
        if (!Enum.TryParse<NivelAtendimento>(req.NovoNivel, out var nivel))
            return DataBadRequest("Nivel invalido.");
        try
        {
            await ticketService.EncaminharAsync(new EncaminharNivelCommand(id, nivel, req.Motivo));
            return DataOk(new { id, nivel = nivel.ToString() });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/bug-fix")]
    public async Task<IActionResult> GerarBugFix(Guid id, [FromBody] BugFixRequest req)
    {
        try
        {
            var bug = await bugFixService.GerarAsync(new GerarBugFixCommand(
                id, req.Titulo, req.Descricao, req.Severidade, req.Componente, req.StackTrace));
            await audit.LogAsync("BugFixGerado", $"OrigemTicketId={id}, NovoTicketId={bug.Id}", null);
            return DataCreated($"/api/admin/tickets/{bug.Id}", new { bug.Id });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/anexos")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Anexar(Guid id, IFormFile file, [FromQuery] Guid? mensagemId = null)
    {
        if (file is null || file.Length == 0) return DataBadRequest("Arquivo obrigatorio.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);

        try
        {
            var anexo = await anexoService.AnexarAsync(new AnexarArquivoCommand(
                id, mensagemId, file.FileName, file.ContentType, ms.ToArray(), IsAdmin: true));
            return DataCreated($"/api/admin/tickets/{id}/anexos/{anexo.Id}", new { anexo.Id, anexo.Url });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTicket(Guid id)
    {
        try
        {
            await ticketService.AlterarStatusAsync(new AlterarStatusTicketCommand(id, TicketStatus.Fechado));
            await audit.LogAsync("TicketFechado", $"TicketId={id}", null);
            return DataOk(new { id, status = "Fechado" });
        }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
    }
}

public record CreateTicketRequest(Guid EmpresaId, string Titulo, string Descricao, string Categoria, string Prioridade, string? Nivel = null);
public record PatchTicketRequest(string? Status, string? Prioridade, Guid? AtendenteId);
public record AddMensagemRequest(string Conteudo, bool Interno = false, IReadOnlyList<Guid>? AnexoIds = null);
public record EncaminharRequest(string NovoNivel, string? Motivo);
public record BugFixRequest(string Titulo, string Descricao, string Severidade, string? Componente, string? StackTrace);
