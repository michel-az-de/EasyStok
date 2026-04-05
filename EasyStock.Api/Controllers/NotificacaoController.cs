using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/notificacoes")]
public class NotificacaoController : ControllerBase
{
    private readonly INotificacaoRepository _notificacaoRepository;
    private readonly IUnitOfWork _unitOfWork;

    public NotificacaoController(INotificacaoRepository notificacaoRepository, IUnitOfWork unitOfWork)
    {
        _notificacaoRepository = notificacaoRepository;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? lida = null,
        [FromQuery] EasyStock.Domain.Enums.TipoAlertaEstoque? tipo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _notificacaoRepository.GetByEmpresaAsync(empresaId, lida, tipo, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("badge")]
    public async Task<IActionResult> GetBadge([FromQuery] Guid empresaId)
    {
        var count = await _notificacaoRepository.CountNaoLidasAsync(empresaId);
        return Ok(new { Count = count });
    }

    [HttpPut("{id}/marcar-lida")]
    [HttpPatch("{id}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id)
    {
        var notificacao = await _notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return NotFound();

        notificacao.MarcarComoLida();
        await _notificacaoRepository.UpdateAsync(notificacao);
        await _unitOfWork.CommitAsync();

        return NoContent();
    }

    [HttpPut("marcar-todas-lidas")]
    [HttpPost("marcar-todas")]
    public async Task<IActionResult> MarcarTodasLidas([FromQuery] Guid empresaId)
    {
        await _notificacaoRepository.MarcarTodasComoLidasAsync(empresaId);
        await _unitOfWork.CommitAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var notificacao = await _notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return NotFound();

        await _notificacaoRepository.DeleteAsync(id);
        await _unitOfWork.CommitAsync();
        return NoContent();
    }
}
