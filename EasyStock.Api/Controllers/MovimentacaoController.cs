using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/movimentacoes")]
public class MovimentacaoController : ControllerBase
{
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository;

    public MovimentacaoController(IMovimentacaoEstoqueRepository movimentacaoRepository)
    {
        _movimentacaoRepository = movimentacaoRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _movimentacaoRepository.GetByEmpresaAsync(
            empresaId, de, ate, tipo, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("item/{itemEstoqueId}")]
    public async Task<IActionResult> GetByItem(Guid itemEstoqueId)
    {
        var items = await _movimentacaoRepository.GetByItemEstoqueAsync(itemEstoqueId);
        return Ok(items);
    }
}
