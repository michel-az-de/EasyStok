using EasyStock.Api.Http;
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
public class ItemEstoqueController(
    IItemEstoqueRepository itemEstoqueRepository,
    RegistrarEntradaEstoqueUseCase registrarEntradaUseCase,
    RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
    ReporEstoqueUseCase reporEstoqueUseCase,
    BuscarEstoqueInteligenteUseCase buscarUseCase) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (itens, totalCount) = await itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, page, pageSize);
        return DataPaged(itens, totalCount, page, pageSize);
    }

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar(
        [FromQuery] Guid empresaId,
        [FromQuery] string termo,
        [FromQuery] int limite = 50)
        => DataOk(await buscarUseCase.ExecuteAsync(new BuscarEstoqueInteligenteQuery(empresaId, termo, limite)));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await itemEstoqueRepository.GetByIdAsync(empresaId, id);
        return item is null ? DataNotFound() : DataOk(item);
    }

    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada(RegistrarEntradaEstoqueCommand command)
    {
        var result = await registrarEntradaUseCase.ExecuteAsync(command);
        return DataCreated($"/api/estoque/{result.ItemEstoqueId}", result);
    }

    [HttpPost("saida")]
    public async Task<IActionResult> RegistrarSaida(RegistrarSaidaEstoqueCommand command)
        => DataOk(await registrarSaidaUseCase.ExecuteAsync(command));

    [HttpGet("{id}/para-reposicao")]
    public async Task<IActionResult> ParaReposicao(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await itemEstoqueRepository.GetItemComProdutoAsync(empresaId, id);
        return item is null ? DataNotFound() : DataOk(item);
    }

    [HttpPost("reposicao")]
    public async Task<IActionResult> ReporEstoque(ReporEstoqueCommand command)
        => DataOk(await reporEstoqueUseCase.ExecuteAsync(command));
}
