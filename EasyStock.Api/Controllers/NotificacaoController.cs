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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _notificacaoRepository.GetByEmpresaAsync(empresaId, lida, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpPut("{id}/marcar-lida")]
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
    public async Task<IActionResult> MarcarTodasLidas([FromQuery] Guid empresaId)
    {
        await _notificacaoRepository.MarcarTodasComoLidasAsync(empresaId);
        return NoContent();
    }
}
