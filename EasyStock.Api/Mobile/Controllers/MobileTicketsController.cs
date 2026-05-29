using System.Text.Json;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Abertura automatica de tickets a partir do app mobile (sem usuario humano
/// logado). Usado por sync.js quando detecta erros graves (auto-pair 3x
/// falhas, sync 5x falhas, exception nao capturada) e quer notificar suporte.
///
/// Diferencas pro TicketsController web:
///  - Auth via X-Mobile-Api-Key (device pareado), nao JWT humano.
///  - empresaId vem do device, nao do JWT.
///  - autorId da mensagem busca PairedByUserId, com fallback pra primeiro
///    usuario ativo da empresa (FK Restrict em admin_ticket_mensagens).
/// </summary>
[ApiController]
[Route("api/mobile/tickets")]
public class MobileTicketsController(
    EasyStockDbContext db,
    ISlaResolver slaResolver,
    INotificadorService notificador,
    ILogger<MobileTicketsController> log) : ControllerBase
{
    public sealed record CreateMobileTicketRequest(
        string Titulo,
        string Descricao,
        string? Categoria,
        string? ErrorLogJson);

    public sealed record CreateMobileTicketResponse(
        Guid TicketId,
        string Status,
        DateTime CriadoEm,
        string Titulo);

    [HttpPost]
    [MobileApiKey]
    [EnableRateLimiting("mobile-anonymous")]
    public async Task<ActionResult<CreateMobileTicketResponse>> Create(
        [FromBody] CreateMobileTicketRequest req,
        CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });
        if (req is null) return BadRequest(new { error = "payload obrigatório" });
        if (string.IsNullOrWhiteSpace(req.Titulo)) return BadRequest(new { error = "titulo obrigatório" });
        if (req.Titulo.Length > 200) return BadRequest(new { error = "titulo excede 200 chars" });
        if (string.IsNullOrWhiteSpace(req.Descricao)) return BadRequest(new { error = "descricao obrigatória" });
        if (req.Descricao.Length > 10000) return BadRequest(new { error = "descricao excede 10000 chars" });

        var categoria = ParseCategoria(req.Categoria) ?? TicketCategoria.Bug;
        var prioridade = TicketPrioridade.Normal;
        var sla = await slaResolver.ResolverAsync(device.EmpresaId, prioridade, ct: ct);

        // Anexa errorLogJson no final da descricao quando informado (truncado).
        var descricaoFinal = req.Descricao;
        if (!string.IsNullOrWhiteSpace(req.ErrorLogJson))
        {
            var trecho = req.ErrorLogJson.Length > 5000
                ? req.ErrorLogJson[..5000] + "...[truncado]"
                : req.ErrorLogJson;
            descricaoFinal += $"\n\n--- ERROR LOG (device {device.Id}) ---\n{trecho}";
        }

        // Autor da mensagem precisa Guid valido (FK admin_ticket_mensagens.autor_id
        // tem DeleteBehavior.Restrict, nao aceita null). Tenta PairedByUserId; se
        // null (caso pair-auto sem operador humano), busca primeiro usuario ativo
        // da empresa. IgnoreQueryFilters porque o endpoint nao tem JWT — sem isso
        // o tenant filter global zera o lookup.
        var autorId = device.PairedByUserId;
        if (autorId is null || autorId == Guid.Empty)
        {
            autorId = await db.Set<UsuarioEmpresa>().IgnoreQueryFilters().AsNoTracking()
                .Where(ue => ue.EmpresaId == device.EmpresaId && ue.Ativo)
                .OrderBy(ue => ue.CriadoEm)
                .Select(ue => (Guid?)ue.UsuarioId)
                .FirstOrDefaultAsync(ct);
        }
        if (autorId is null || autorId == Guid.Empty)
        {
            log.LogError("Mobile ticket: empresa {EmpresaId} sem usuario ativo — impossivel anexar autor da mensagem",
                device.EmpresaId);
            return StatusCode(500, new { error = "empresa sem usuario ativo — não posso criar ticket" });
        }

        var ticket = AdminTicket.Criar(
            empresaId: device.EmpresaId,
            titulo: req.Titulo.Trim(),
            descricao: descricaoFinal,
            categoria: categoria,
            prioridade: prioridade,
            prazoResposta: sla.PrazoResposta,
            prazoResolucao: sla.PrazoResolucao,
            criadoPorId: autorId);

        ticket.Mensagens.Add(AdminTicketMensagem.Criar(
            ticketId: ticket.Id,
            autorId: autorId.Value,
            conteudo: descricaoFinal,
            isAdmin: false));

        db.Set<AdminTicket>().Add(ticket);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Mobile ticket criado: id={TicketId} empresa={EmpresaId} device={DeviceId} categoria={Categoria}",
            ticket.Id, device.EmpresaId, device.Id, categoria);

        // Notificacao best-effort fora da transacao principal.
        try
        {
            await notificador.PublicarEventoAsync(
                TipoEventoNotificacao.TicketCriado,
                device.EmpresaId,
                usuarioDestinoId: null,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    prioridade = ticket.Prioridade.ToString(),
                    categoria = ticket.Categoria.ToString(),
                    abertoPorMobile = true,
                    deviceId = device.Id
                }),
                ct: ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao publicar evento ticket mobile {TicketId}", ticket.Id);
        }

        return Ok(new CreateMobileTicketResponse(
            TicketId: ticket.Id,
            Status: ticket.Status.ToString(),
            CriadoEm: ticket.CriadoEm,
            Titulo: ticket.Titulo));
    }

    private static TicketCategoria? ParseCategoria(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<TicketCategoria>(raw, true, out var c) ? c : null;
    }
}
