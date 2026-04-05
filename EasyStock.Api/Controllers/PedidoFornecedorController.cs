using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.PedidoFornecedor;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/pedidos-fornecedor")]
[Authorize]
public class PedidoFornecedorController(
    CriarPedidoFornecedorUseCase criarUseCase,
    AtualizarPedidoFornecedorUseCase atualizarUseCase,
    ListarPedidosFornecedorUseCase listarUseCase,
    ObterPedidoFornecedorUseCase obterUseCase,
    TransicionarStatusPedidoFornecedorUseCase transicionarStatusUseCase,
    ReceberPedidoFornecedorUseCase receberUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? fornecedorId = null,
        [FromQuery] StatusPedidoFornecedor? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TemAcesso(empresaId)) return Forbid();

        var (pedidos, total) = await listarUseCase.ExecuteAsync(
            new ListarPedidosFornecedorQuery(empresaId, fornecedorId, status, page, pageSize));
        return DataPaged(pedidos, total, page, pageSize);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TemAcesso(empresaId)) return Forbid();

        var pedido = await obterUseCase.ExecuteAsync(new ObterPedidoFornecedorQuery(empresaId, id));
        return DataOk(pedido);
    }

    [HttpPost]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Create([FromBody] CriarPedidoFornecedorCommand command)
    {
        if (!TemAcesso(command.EmpresaId)) return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/pedidos-fornecedor/{resultado.Id}", resultado);
    }

    [HttpPatch("{id}")]
    [HttpPut("{id}")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarPedidoFornecedorCommand command)
    {
        if (!TemAcesso(command.EmpresaId)) return Forbid();
        if (id != command.PedidoId)
            return DataBadRequest("PedidoId da rota difere do corpo.");

        var resultado = await atualizarUseCase.ExecuteAsync(command);
        return DataOk(resultado);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> TransicionarStatus(
        Guid id,
        [FromBody] TransicionarStatusPedidoFornecedorCommand command)
    {
        if (!TemAcesso(command.EmpresaId)) return Forbid();
        if (id != command.PedidoId)
            return DataBadRequest("PedidoId da rota difere do corpo.");

        await transicionarStatusUseCase.ExecuteAsync(command);
        return NoContent();
    }

    [HttpPost("{id}/receber")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Receber(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TemAcesso(empresaId)) return Forbid();

        var resultado = await receberUseCase.ExecuteAsync(new ReceberPedidoFornecedorCommand(id, empresaId));
        return DataOk(resultado);
    }

    private bool TemAcesso(Guid empresaId) =>
        currentUser.Nivel == NivelAcesso.SuperAdmin ||
        currentUser.EmpresaId == Guid.Empty ||
        currentUser.EmpresaId == empresaId;
}
