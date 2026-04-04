using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/inteligencia")]
public class InteligenciaController : ControllerBase
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository;

    public InteligenciaController(IItemEstoqueRepository itemEstoqueRepository)
    {
        _itemEstoqueRepository = itemEstoqueRepository;
    }

    [HttpGet("estoque-baixo")]
    public async Task<IActionResult> EstoqueBaixo([FromQuery] Guid empresaId, [FromQuery] int limite = 10, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, limite, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("proximo-vencimento")]
    public async Task<IActionResult> ProximoVencimento([FromQuery] Guid empresaId, [FromQuery] int dias = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, dias, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("parados")]
    public async Task<IActionResult> ItensParados([FromQuery] Guid empresaId, [FromQuery] int diasSemMovimento = 90, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _itemEstoqueRepository.GetItensParadosAsync(empresaId, diasSemMovimento, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("sugestao-reposicao")]
    public async Task<IActionResult> SugestaoReposicao([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }
}