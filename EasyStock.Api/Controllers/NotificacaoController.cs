using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
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
    [SwaggerOperation(Summary = "List notifications (paginated)", Description = "Filter by read status and alert type.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? lida = null,
        [FromQuery] EasyStock.Domain.Enums.TipoAlertaEstoque? tipo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await notificacaoRepository.GetByEmpresaAsync(resolvedEmpresaId, lida, tipo, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
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

    [SwaggerOperation(Summary = "Mark notification as read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}/marcar-lida")]
    [HttpPatch("{id}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id)
    {
        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return DataNotFound();
        if (!TryResolveEmpresaId(currentUser, notificacao.EmpresaId, out _, out var error))
            return error!;

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
    public async Task<IActionResult> Delete(Guid id)
    {
        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return DataNotFound();
        if (!TryResolveEmpresaId(currentUser, notificacao.EmpresaId, out _, out var error))
            return error!;

        await notificacaoRepository.DeleteAsync(id);
        await unitOfWork.CommitAsync();
        return NoContent();
    }
}
