using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AdicionarItemLote;
using EasyStock.Application.UseCases.ConferirEtiqueta;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.ListarLotes;
using EasyStock.Application.UseCases.ObterLoteDetalhes;
using EasyStock.Application.UseCases.RemoverItemLote;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Lotes (produção + etiquetas)")]
[ApiController]
[Route("api/lotes")]
[Authorize]
[ValidateEmpresaId]
public class LotesController(
    CriarLoteUseCase criarUseCase,
    AdicionarItemLoteUseCase addItemUseCase,
    RemoverItemLoteUseCase removeItemUseCase,
    FinalizarLoteUseCase finalizarUseCase,
    ListarLotesUseCase listarUseCase,
    ObterLoteDetalhesUseCase obterUseCase,
    ConferirEtiquetaUseCase conferirUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List production batches (paginated)")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? desde = null, [FromQuery] DateTime? ate = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "dataproducao", [FromQuery] string? order = "desc")
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarUseCase.ExecuteAsync(new ListarLotesQuery(
            emp, p, sz, status, desde, ate, search, sort, NormaliseOrder(order)));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Get batch details (items + labels)")]
    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await obterUseCase.ExecuteAsync(new ObterLoteDetalhesQuery(emp, id));
        return result == null ? DataNotFound("Lote não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Create batch (auto-generates LOT-YYMMDD-NNN)")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Create([FromBody] CriarLoteCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await criarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            OperadorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/lotes/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Add item to batch")]
    [HttpPost("{id}/itens")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AdicionarItemLoteCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await addItemUseCase.ExecuteAsync(command with { EmpresaId = emp, LoteId = id });
        return result == null ? DataNotFound("Lote não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Remove item from batch")]
    [HttpDelete("{id}/itens/{itemId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await removeItemUseCase.ExecuteAsync(new RemoverItemLoteCommand(emp, id, itemId));
        return result == null ? DataNotFound("Lote não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Finalize batch (generates 1 label per unit)")]
    [HttpPost("{id}/finalizar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Finalizar(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await finalizarUseCase.ExecuteAsync(new FinalizarLoteCommand(emp, id));
        return result == null ? DataNotFound("Lote não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Confirm a label by code (scanner)")]
    [HttpPost("etiquetas/conferir")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ConferirEtiqueta([FromBody] ConferirEtiquetaCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await conferirUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            ConferidaPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null
        });
        return result == null ? DataNotFound("Etiqueta não encontrada.") : DataOk(result);
    }
}
