using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/estoque")]
public class ItemEstoqueController : ControllerBase
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository;
    private readonly EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase _registrarEntradaUseCase;
    private readonly EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase _registrarSaidaUseCase;
    private readonly EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase _reporEstoqueUseCase;
    private readonly BuscarEstoqueInteligenteUseCase _buscarUseCase;

    public ItemEstoqueController(
        IItemEstoqueRepository itemEstoqueRepository,
        EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase registrarEntradaUseCase,
        EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
        EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase reporEstoqueUseCase,
        BuscarEstoqueInteligenteUseCase buscarUseCase)
    {
        _itemEstoqueRepository = itemEstoqueRepository;
        _registrarEntradaUseCase = registrarEntradaUseCase;
        _registrarSaidaUseCase = registrarSaidaUseCase;
        _reporEstoqueUseCase = reporEstoqueUseCase;
        _buscarUseCase = buscarUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (itens, totalCount) = await _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, page, pageSize);
        return Ok(new { Items = itens, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] Guid empresaId, [FromQuery] string termo, [FromQuery] int limite = 50)
    {
        var query = new BuscarEstoqueInteligenteQuery(empresaId, termo, limite);
        var resultado = await _buscarUseCase.ExecuteAsync(query);
        return Ok(resultado);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await _itemEstoqueRepository.GetByIdAsync(empresaId, id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada(RegistrarEntradaEstoqueCommand command)
    {
        var result = await _registrarEntradaUseCase.ExecuteAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = result.ItemEstoqueId }, result);
    }

    [HttpPost("saida")]
    public async Task<IActionResult> RegistrarSaida(RegistrarSaidaEstoqueCommand command)
    {
        var result = await _registrarSaidaUseCase.ExecuteAsync(command);
        return Ok(result);
    }

    [HttpGet("{id}/para-reposicao")]
    public async Task<IActionResult> ParaReposicao(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await _itemEstoqueRepository.GetItemComProdutoAsync(empresaId, id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost("reposicao")]
    public async Task<IActionResult> ReporEstoque(ReporEstoqueCommand command)
    {
        var result = await _reporEstoqueUseCase.ExecuteAsync(command);
        return Ok(result);
    }
}
