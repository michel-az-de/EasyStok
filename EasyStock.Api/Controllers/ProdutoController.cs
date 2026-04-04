using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/produtos")]
public class ProdutoController : ControllerBase
{
    private readonly IProdutoRepository _produtoRepository;
    private readonly CadastrarProdutoUseCase _cadastrarProdutoUseCase;

    public ProdutoController(IProdutoRepository produtoRepository, CadastrarProdutoUseCase cadastrarProdutoUseCase)
    {
        _produtoRepository = produtoRepository;
        _cadastrarProdutoUseCase = cadastrarProdutoUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (produtos, totalCount) = await _produtoRepository.GetProdutosPaginadosAsync(empresaId, page, pageSize);
        return Ok(new { Produtos = produtos, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var produto = await _produtoRepository.GetByIdAsync(id);
        if (produto == null) return NotFound();
        return Ok(produto);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] Guid empresaId, [FromQuery] string termo)
    {
        var produtos = await _produtoRepository.SearchAsync(empresaId, termo);
        return Ok(produtos);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CadastrarProdutoCommand command)
    {
        var result = await _cadastrarProdutoUseCase.ExecuteAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = result.ProdutoId }, result);
    }
}
