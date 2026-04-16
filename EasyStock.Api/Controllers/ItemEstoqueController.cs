using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.EstornarSaida;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Inventory / Estoque")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/estoque")]
public class ItemEstoqueController(
    IItemEstoqueRepository itemEstoqueRepository,
    RegistrarEntradaEstoqueUseCase registrarEntradaUseCase,
    RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
    EstornarSaidaUseCase estornarSaidaUseCase,
    ReporEstoqueUseCase reporEstoqueUseCase,
    BuscarEstoqueInteligenteUseCase buscarUseCase,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
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
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (p, ps) = NormalisePage(page, pageSize);
        var (itens, totalCount) = await itemEstoqueRepository.GetItensEstoquePaginadosAsync(resolvedEmpresaId, p, ps);

        var dtos = itens.Select(MapItemToDto);

        return DataPaged(dtos, totalCount, p, ps);
    }

    private static object MapItemToDto(EasyStock.Domain.Entities.ItemEstoque i) => new
    {
        id = i.Id.ToString(),
        produtoId = i.ProdutoId.ToString(),
        varId = (i.ProdutoVariacaoId ?? Guid.Empty).ToString(),
        sku = i.Produto != null ? (i.Produto.SkuBase?.Value ?? "") : (i.CodigoInterno ?? ""),
        qty = (int)i.QuantidadeAtual,
        entryDate = DateOnly.FromDateTime(i.EntradaEm),
        lastMov = new DateTimeOffset(i.UltimaMovimentacaoEm ?? i.EntradaEm, TimeSpan.Zero),
        validade = i.ValidadeEm != null ? DateOnly.FromDateTime(i.ValidadeEm.DataValidade) : (DateOnly?)null,
        lote = i.CodigoLote?.Value,
        vel = i.VelocidadeSaidaDiaria,
        stopped = i.DiasSemMovimentacao,
        status = MapStatus(i.Status),
        custoUnitario = (decimal)i.CustoUnitario,
        precoVendaSugerido = i.PrecoVendaSugerido != null ? (decimal?)i.PrecoVendaSugerido : null,
        produto = i.Produto != null ? new
        {
            id = i.Produto.Id.ToString(),
            sku = i.Produto.SkuBase?.Value ?? "",
            nome = i.Produto.Nome,
            emoji = (string?)null,
            categoria = i.Produto.CategoriaId.ToString(),
            status = i.Produto.Status.ToString()
        } : null,
        variacao = i.ProdutoVariacao != null ? new
        {
            id = i.ProdutoVariacao.Id.ToString(),
            produtoId = i.ProdutoVariacao.ProdutoId.ToString(),
            nome = i.ProdutoVariacao.Nome
        } : null
    };

    private static string MapStatus(EasyStock.Domain.Enums.StatusItemEstoque status) => status switch
    {
        EasyStock.Domain.Enums.StatusItemEstoque.Ok => "ok",
        EasyStock.Domain.Enums.StatusItemEstoque.Warn => "atencao",
        EasyStock.Domain.Enums.StatusItemEstoque.Critical => "critico",
        EasyStock.Domain.Enums.StatusItemEstoque.Slow => "parado",
        EasyStock.Domain.Enums.StatusItemEstoque.Vencido => "vencido",
        EasyStock.Domain.Enums.StatusItemEstoque.Descartado => "descartado",
        EasyStock.Domain.Enums.StatusItemEstoque.Bloqueado => "bloqueado",
        _ => "ok"
    };

    [SwaggerOperation(Summary = "Smart inventory search", Description = "Full-text search across product name, SKU, barcode and internal code.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar(
        [FromQuery] Guid empresaId,
        [FromQuery] string termo,
        [FromQuery] int limite = 50)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await buscarUseCase.ExecuteAsync(new BuscarEstoqueInteligenteQuery(resolvedEmpresaId, termo, limite)));
    }

    [SwaggerOperation(Summary = "Get stock item details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var item = await itemEstoqueRepository.GetItemComProdutoAsync(resolvedEmpresaId, id);
        return item is null ? DataNotFound() : DataOk(MapItemToDto(item));
    }

    [SwaggerOperation(Summary = "Register stock entry", Description = "Adds quantity to stock and creates a movement record. Updates running cost average.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada(RegistrarEntradaEstoqueCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await registrarEntradaUseCase.ExecuteAsync(command with { EmpresaId = resolvedEmpresaId });
        return DataCreated($"/api/estoque/{result.ItemEstoqueId}", result);
    }

    [SwaggerOperation(Summary = "Register stock exit", Description = "Removes quantity from stock. Validates minimum stock levels.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("saida")]
    public async Task<IActionResult> RegistrarSaida(RegistrarSaidaEstoqueCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await registrarSaidaUseCase.ExecuteAsync(command with { EmpresaId = resolvedEmpresaId }));
    }

    [SwaggerOperation(Summary = "Get stock items by product")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("por-produto/{produtoId}")]
    public async Task<IActionResult> GetByProduto(Guid produtoId, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var items = await itemEstoqueRepository.GetByProdutoAsync(resolvedEmpresaId, produtoId);
        return DataOk(items.Select(MapItemToDto));
    }

    [SwaggerOperation(Summary = "Get stock item replenishment data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/para-reposicao")]
    public async Task<IActionResult> ParaReposicao(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var item = await itemEstoqueRepository.GetItemComProdutoAsync(resolvedEmpresaId, id);
        return item is null ? DataNotFound() : DataOk(item);
    }

    [SwaggerOperation(Summary = "Replenish stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("reposicao")]
    public async Task<IActionResult> ReporEstoque(ReporEstoqueCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await reporEstoqueUseCase.ExecuteAsync(command with { EmpresaId = resolvedEmpresaId }));
    }

    [SwaggerOperation(Summary = "Reverse a stock exit (estorno)", Description = "Creates a reversal entry restoring the stock quantity.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("estorno/{movimentacaoId}")]
    public async Task<IActionResult> Estornar(Guid movimentacaoId, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await estornarSaidaUseCase.ExecuteAsync(
            new EstornarSaidaCommand(resolvedEmpresaId, movimentacaoId, null));
        return DataOk(result);
    }
}
