using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.ListasCompras;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Listas de compras")]
[ApiController]
[Route("api/listas-compras")]
[Authorize]
[ValidateEmpresaId]
public class ListasComprasController(
    CriarListaComprasUseCase criarUseCase,
    ListarListasComprasUseCase listarUseCase,
    ObterListaComprasUseCase obterUseCase,
    ArquivarListaComprasUseCase arquivarUseCase,
    ReabrirListaComprasUseCase reabrirUseCase,
    AdicionarItemListaComprasUseCase addItemUseCase,
    ToggleItemListaComprasUseCase toggleItemUseCase,
    RemoverItemListaComprasUseCase removeItemUseCase,
    GerarListaComprasUseCase gerarUseCase,
    GerarPedidosDaListaUseCase gerarPedidosUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List shopping lists (paginated)")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        [FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarUseCase.ExecuteAsync(new ListarListasComprasQuery(emp, p, sz, status, search));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Get list with items")]
    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await obterUseCase.ExecuteAsync(new ObterListaComprasQuery(emp, id));
        return result == null ? DataNotFound("Lista não encontrada.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Create list")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Create([FromBody] CriarListaComprasCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await criarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            CriadaPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/listas-compras/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Create list pre-populated with items (e.g. from low-stock suggestion)")]
    [HttpPost("gerar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Gerar([FromBody] GerarListaComprasCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await gerarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            CriadaPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/listas-compras/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Generate supplier orders from a list (grouped by preferred supplier; notifies suppliers)")]
    [HttpPost("{id}/gerar-pedidos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GerarPedidos(
        Guid id,
        [FromQuery] Guid? empresaId,
        [FromQuery] Guid? lojaId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString() : idempotencyKey;
        var result = await gerarPedidosUseCase.ExecuteAsync(
            new GerarPedidosDaListaCommand(emp, id, lojaId, key), ct);
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Archive list")]
    [HttpPost("{id}/arquivar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Arquivar(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await arquivarUseCase.ExecuteAsync(new ArquivarListaComprasCommand(emp, id));
        return result == null ? DataNotFound() : DataOk(result);
    }

    [SwaggerOperation(Summary = "Reopen list")]
    [HttpPost("{id}/reabrir")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Reabrir(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await reabrirUseCase.ExecuteAsync(new ReabrirListaComprasCommand(emp, id));
        return result == null ? DataNotFound() : DataOk(result);
    }

    [SwaggerOperation(Summary = "Add item to list")]
    [HttpPost("{id}/itens")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AdicionarItemListaComprasCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await addItemUseCase.ExecuteAsync(command with { EmpresaId = emp, ListaComprasId = id });
        return result == null ? DataNotFound() : DataOk(result);
    }

    [SwaggerOperation(Summary = "Toggle item done/undone")]
    [HttpPatch("{id}/itens/{itemId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ToggleItem(Guid id, Guid itemId, [FromBody] ToggleItemBody body)
    {
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var emp, out var err)) return err!;
        // Nome do usuário vem no body (Web Service injeta via SessionService).
        var result = await toggleItemUseCase.ExecuteAsync(new ToggleItemListaComprasCommand(
            emp, id, itemId, body.Done,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null, body.UsuarioNome));
        return result == null ? DataNotFound() : DataOk(result);
    }

    [SwaggerOperation(Summary = "Remove item")]
    [HttpDelete("{id}/itens/{itemId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await removeItemUseCase.ExecuteAsync(new RemoverItemListaComprasCommand(emp, id, itemId));
        return ok ? NoContent() : DataNotFound();
    }
}

public sealed record ToggleItemBody(Guid? EmpresaId, bool Done, string? UsuarioNome);
