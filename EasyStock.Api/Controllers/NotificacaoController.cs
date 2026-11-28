using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>#3400: os avisos que o Desfazer do sino volta a não lidos (os ids que o marcar lido ou o marcar-todas mudou).</summary>
public sealed record MarcarNaoLidasRequest(IReadOnlyList<Guid>? Ids);

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

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, totalCount) = await notificacaoRepository.GetByEmpresaAsync(resolvedEmpresaId, lida, tipo, severidade, page, pageSize, currentUser.UsuarioId);
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

        var count = await notificacaoRepository.CountNaoLidasAsync(resolvedEmpresaId, currentUser.UsuarioId);
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

        var resumo = await notificacaoRepository.GetResumoAsync(resolvedEmpresaId, currentUser.UsuarioId);
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

        var items = await notificacaoRepository.GetRecentesNaoLidasAsync(resolvedEmpresaId, Math.Clamp(limit, 1, 10), currentUser.UsuarioId);
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
        if (notificacao == null || notificacao.EmpresaId != resolvedEmpresaId
            || notificacao.UsuarioId.HasValue && notificacao.UsuarioId != currentUser.UsuarioId) return DataNotFound();

        notificacao.MarcarComoLida();
        await notificacaoRepository.UpdateAsync(notificacao);
        await unitOfWork.CommitAsync();

        return NoContent();
    }

    [SwaggerOperation(Summary = "Mark all notifications as read", Description = "Returns the ids it changed, for the bell's undo.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("marcar-todas-lidas")]
    [HttpPost("marcar-todas")]
    public async Task<IActionResult> MarcarTodasLidas([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var ids = await notificacaoRepository.MarcarTodasComoLidasAsync(resolvedEmpresaId, currentUser.UsuarioId);
        await unitOfWork.CommitAsync();
        return DataOk(new { ids });
    }

    [SwaggerOperation(Summary = "Mark notifications as unread (undo)",
        Description = "Only read notifications of the company and of the caller (or general ones) go back to unread.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("nao-lidas")]
    public async Task<IActionResult> MarcarNaoLidas([FromBody] MarcarNaoLidasRequest? request, [FromQuery] Guid empresaId)
    {
        if (request?.Ids is not { Count: > 0 } ids)
            return DataBadRequest("Informe os avisos que voltam a não lidos.");
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        await notificacaoRepository.MarcarComoNaoLidasAsync(resolvedEmpresaId, ids, currentUser.UsuarioId);
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
        if (notificacao == null || notificacao.EmpresaId != resolvedEmpresaId
            || notificacao.UsuarioId.HasValue && notificacao.UsuarioId != currentUser.UsuarioId) return DataNotFound();

        await notificacaoRepository.DeleteAsync(resolvedEmpresaId, id);
        await unitOfWork.CommitAsync();
        return NoContent();
    }
}
