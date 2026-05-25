using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Notifications / Notificações")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/notificacoes")]
public class NotificacaoController(
    INotificacaoRepository notificacaoRepository,
    IUnitOfWork unitOfWork,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List notifications (paginated)", Description = "Filter by read status, alert type and severity.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? lida = null,
        [FromQuery] TipoAlertaEstoque? tipo = null,
        [FromQuery] SeveridadeNotificacao? severidade = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await notificacaoRepository.GetByEmpresaAsync(resolvedEmpresaId, lida, tipo, severidade, page, pageSize);
        var dtos = items.Select(n => new
        {
            id = n.Id.ToString(),
            tipo = n.TipoAlerta.ToString(),
            titulo = n.Titulo,
            mensagem = n.Mensagem,
            severidade = n.Severidade.ToString(),
            referenciaId = n.ReferenciaId?.ToString(),
            lida = n.Lida,
            createdAt = new DateTimeOffset(n.CriadaEm, TimeSpan.Zero)
        });
        return DataPaged(dtos, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get unread notification count", Description = "Lightweight endpoint for badge counters.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("badge")]
    public async Task<IActionResult> GetBadge([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var count = await notificacaoRepository.CountNaoLidasAsync(resolvedEmpresaId);
        return DataOk(new { count });
    }

    [SwaggerOperation(Summary = "Get notification summary", Description = "Counts by severity and type for unread notifications.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("resumo")]
    public async Task<IActionResult> GetResumo([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var resumo = await notificacaoRepository.GetResumoAsync(resolvedEmpresaId);
        return DataOk(resumo);
    }

    [SwaggerOperation(Summary = "Get recent unread notifications", Description = "Top 5 unread sorted by severity for topbar dropdown.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("recentes")]
    public async Task<IActionResult> GetRecentes([FromQuery] Guid empresaId, [FromQuery] int limit = 5)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var items = await notificacaoRepository.GetRecentesNaoLidasAsync(resolvedEmpresaId, Math.Min(limit, 10));
        var dtos = items.Select(n => new
        {
            id = n.Id.ToString(),
            tipo = n.TipoAlerta.ToString(),
            titulo = n.Titulo,
            mensagem = n.Mensagem,
            severidade = n.Severidade.ToString(),
            referenciaId = n.ReferenciaId?.ToString(),
            lida = n.Lida,
            createdAt = new DateTimeOffset(n.CriadaEm, TimeSpan.Zero)
        });
        return DataOk(dtos);
    }

    [SwaggerOperation(Summary = "Mark notification as read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}/marcar-lida")]
    [HttpPatch("{id}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out _))
            return DataNotFound();

        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null || notificacao.EmpresaId != resolvedEmpresaId) return DataNotFound();

        notificacao.MarcarComoLida();
        await notificacaoRepository.UpdateAsync(notificacao);
        await unitOfWork.CommitAsync();

        return NoContent();
    }

    [SwaggerOperation(Summary = "Mark all notifications as read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("marcar-todas-lidas")]
    [HttpPost("marcar-todas")]
    public async Task<IActionResult> MarcarTodasLidas([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        await notificacaoRepository.MarcarTodasComoLidasAsync(resolvedEmpresaId);
        await unitOfWork.CommitAsync();
        return NoContent();
    }

    [SwaggerOperation(Summary = "Delete notification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out _))
            return DataNotFound();

        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null || notificacao.EmpresaId != resolvedEmpresaId) return DataNotFound();

        await notificacaoRepository.DeleteAsync(resolvedEmpresaId, id);
        await unitOfWork.CommitAsync();
        return NoContent();
    }
}
