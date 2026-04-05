using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Inventory / Estoque")]
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
    [SwaggerOperation(Summary = "List stock items (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (itens, totalCount) = await itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, page, pageSize);
        return DataPaged(itens, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Smart inventory search", Description = "Full-text search across product name, SKU, barcode and internal code.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar(
        [FromQuery] Guid empresaId,
        [FromQuery] string termo,
        [FromQuery] int limite = 50)
        => DataOk(await buscarUseCase.ExecuteAsync(new BuscarEstoqueInteligenteQuery(empresaId, termo, limite)));

    [SwaggerOperation(Summary = "Get stock item details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await itemEstoqueRepository.GetByIdAsync(empresaId, id);
        return item is null ? DataNotFound() : DataOk(item);
    }

    [SwaggerOperation(Summary = "Register stock entry", Description = "Adds quantity to stock and creates a movement record. Updates running cost average.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada(RegistrarEntradaEstoqueCommand command)
    {
        var result = await registrarEntradaUseCase.ExecuteAsync(command);
        return DataCreated($"/api/estoque/{result.ItemEstoqueId}", result);
    }

    [SwaggerOperation(Summary = "Register stock exit", Description = "Removes quantity from stock. Validates minimum stock levels.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("saida")]
    public async Task<IActionResult> RegistrarSaida(RegistrarSaidaEstoqueCommand command)
        => DataOk(await registrarSaidaUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Get stock item replenishment data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/para-reposicao")]
    public async Task<IActionResult> ParaReposicao(Guid id, [FromQuery] Guid empresaId)
    {
        var item = await itemEstoqueRepository.GetItemComProdutoAsync(empresaId, id);
        return item is null ? DataNotFound() : DataOk(item);
    }

    [SwaggerOperation(Summary = "Replenish stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("reposicao")]
    public async Task<IActionResult> ReporEstoque(ReporEstoqueCommand command)
        => DataOk(await reporEstoqueUseCase.ExecuteAsync(command));
}
