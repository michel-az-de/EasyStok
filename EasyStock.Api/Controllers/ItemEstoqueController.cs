using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/estoque")]
public class ItemEstoqueController : ControllerBase
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository;
    private readonly EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase _registrarEntradaUseCase;
    private readonly EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase _registrarSaidaUseCase;
    private readonly EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase _reporEstoqueUseCase;

    public ItemEstoqueController(
        IItemEstoqueRepository itemEstoqueRepository,
        EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase registrarEntradaUseCase,
        EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
        EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase reporEstoqueUseCase)
    {
        _itemEstoqueRepository = itemEstoqueRepository;
        _registrarEntradaUseCase = registrarEntradaUseCase;
        _registrarSaidaUseCase = registrarSaidaUseCase;
        _reporEstoqueUseCase = reporEstoqueUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var itens = await _itemEstoqueRepository.GetAllAsync();
        return Ok(itens);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _itemEstoqueRepository.GetByIdAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ItemEstoque item)
    {
        await _itemEstoqueRepository.AddAsync(item);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, ItemEstoque item)
    {
        if (id != item.Id) return BadRequest();
        await _itemEstoqueRepository.UpdateAsync(item);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _itemEstoqueRepository.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada(RegistrarEntradaEstoqueCommand command)
    {
        var result = await _registrarEntradaUseCase.ExecuteAsync(command);
        return Ok(result);
    }

    [HttpPost("saida")]
    public async Task<IActionResult> RegistrarSaida(RegistrarSaidaEstoqueCommand command)
    {
        var result = await _registrarSaidaUseCase.ExecuteAsync(command);
        return Ok(result);
    }

    [HttpPost("reposicao")]
    public async Task<IActionResult> ReporEstoque(ReporEstoqueCommand command)
    {
        var result = await _reporEstoqueUseCase.ExecuteAsync(command);
        return Ok(result);
    }
}