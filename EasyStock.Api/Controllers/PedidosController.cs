using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AdicionarItemPedido;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.AlterarAgendamentoPedido;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.ListarPedidosCliente;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Application.UseCases.RemoverPagamentoPedido;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Pedidos (encomendas)")]
[ApiController]
[Route("api/pedidos")]
[Authorize]
[ValidateEmpresaId]
public class PedidosController(
    CriarPedidoUseCase criarUseCase,
    AtualizarStatusPedidoUseCase statusUseCase,
    CancelarPedidoUseCase cancelarUseCase,
    AlterarAgendamentoPedidoUseCase agendamentoUseCase,
    ListarPedidosUseCase listarUseCase,
    ObterPedidoDetalhesUseCase obterUseCase,
    AdicionarItemPedidoUseCase addItemUseCase,
    RemoverItemPedidoUseCase removeItemUseCase,
    RegistrarPagamentoPedidoUseCase addPagUseCase,
    RemoverPagamentoPedidoUseCase removePagUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List orders (paginated, filterable)")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? clienteId = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? ate = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "criadoem",
        [FromQuery] string? order = "desc")
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarUseCase.ExecuteAsync(new ListarPedidosQuery(
            emp, p, sz, status, clienteId, desde, ate, search, sort, NormaliseOrder(order)));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Get order details (items + events + payments)")]
    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await obterUseCase.ExecuteAsync(new ObterPedidoDetalhesQuery(emp, id));
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Create order")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Create([FromBody] CriarPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await criarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            CriadoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/pedidos/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Update order status (aguardando→preparando→pronto→entregue)")]
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] AtualizarStatusPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await statusUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            Id = id,
            UsuarioId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Cancel order")]
    [HttpPost("{id}/cancelar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await cancelarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            Id = id,
            UsuarioId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Alter or remove scheduling date")]
    [HttpPatch("{id}/agendamento")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AlterarAgendamento(Guid id, [FromBody] AlterarAgendamentoPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await agendamentoUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            PedidoId = id,
            UsuarioId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    // ── Itens ──────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Add item to order")]
    [HttpPost("{id}/itens")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AdicionarItemPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await addItemUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            PedidoId = id,
            UsuarioId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Remove item")]
    [HttpDelete("{id}/itens/{itemId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await removeItemUseCase.ExecuteAsync(new RemoverItemPedidoCommand(
            emp, id, itemId,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            null, "web"));
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    // ── Pagamentos ─────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Register payment (parcial ou total)")]
    [HttpPost("{id}/pagamentos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddPagamento(Guid id, [FromBody] RegistrarPagamentoPedidoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await addPagUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            PedidoId = id,
            RegistradoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Remove payment")]
    [HttpDelete("{id}/pagamentos/{pagamentoId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemovePagamento(Guid id, Guid pagamentoId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await removePagUseCase.ExecuteAsync(new RemoverPagamentoPedidoCommand(
            emp, id, pagamentoId,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            null, "web"));
        return result == null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
    }
}
