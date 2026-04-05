using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/notificacoes")]
public class NotificacaoController(
    INotificacaoRepository notificacaoRepository,
    IUnitOfWork unitOfWork) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? lida = null,
        [FromQuery] EasyStock.Domain.Enums.TipoAlertaEstoque? tipo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await notificacaoRepository.GetByEmpresaAsync(empresaId, lida, tipo, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [HttpGet("badge")]
    public async Task<IActionResult> GetBadge([FromQuery] Guid empresaId)
    {
        var count = await notificacaoRepository.CountNaoLidasAsync(empresaId);
        return DataOk(new { count });
    }

    [HttpPut("{id}/marcar-lida")]
    [HttpPatch("{id}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id)
    {
        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return DataNotFound();

        notificacao.MarcarComoLida();
        await notificacaoRepository.UpdateAsync(notificacao);
        await unitOfWork.CommitAsync();

        return NoContent();
    }

    [HttpPut("marcar-todas-lidas")]
    [HttpPost("marcar-todas")]
    public async Task<IActionResult> MarcarTodasLidas([FromQuery] Guid empresaId)
    {
        await notificacaoRepository.MarcarTodasComoLidasAsync(empresaId);
        await unitOfWork.CommitAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var notificacao = await notificacaoRepository.GetByIdAsync(id);
        if (notificacao == null) return DataNotFound();

        await notificacaoRepository.DeleteAsync(id);
        await unitOfWork.CommitAsync();
        return NoContent();
    }
}
