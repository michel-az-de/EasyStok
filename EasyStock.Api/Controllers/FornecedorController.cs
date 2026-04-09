using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarFornecedor;
using EasyStock.Application.UseCases.CriarFornecedor;
using EasyStock.Application.UseCases.DesativarFornecedor;
using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Application.UseCases.ListarFornecedores;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Suppliers / Fornecedores")]
[ApiController]
[Route("api/fornecedores")]
[Authorize]
public class FornecedorController(
    CriarFornecedorUseCase criarUseCase,
    AtualizarFornecedorUseCase atualizarUseCase,
    DesativarFornecedorUseCase desativarUseCase,
    ListarFornecedoresUseCase listarUseCase,
    ObterFornecedorDetalheUseCase obterDetalheUseCase,
    ObterHistoricoFornecedorUseCase obterHistoricoUseCase,
    ObterEstatisticasFornecedorUseCase obterEstatisticasUseCase,
    ListarPedidosAbertosUseCase listarPedidosAbertosUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List suppliers (paginated)", Description = "Supports filtering by active status and text search. Requires Operador role.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? ativo = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "nome",
        [FromQuery] string? order = "asc")
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var (fornecedores, total) = await listarUseCase.ExecuteAsync(
            new ListarFornecedoresQuery(empresaId, page, pageSize, ativo, search, sort, NormaliseOrder(order)));
        return DataPaged(fornecedores, total, page, pageSize);
    }

    [SwaggerOperation(Summary = "List open purchase orders across all suppliers (Operador only)", Description = "Returns all pedidos with status Aberto or EmTransito, including supplier name.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("pedidos-abertos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetPedidosAbertos([FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await listarPedidosAbertosUseCase.ExecuteAsync(new ListarPedidosAbertosQuery(empresaId)));
    }

    [SwaggerOperation(Summary = "Get supplier details (Operador only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var fornecedor = await obterDetalheUseCase.ExecuteAsync(new ObterFornecedorDetalheQuery(empresaId, id));
        return DataOk(fornecedor);
    }

    [SwaggerOperation(Summary = "Get supplier purchase history (Operador only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/historico")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetHistorico(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await obterHistoricoUseCase.ExecuteAsync(new ObterHistoricoFornecedorQuery(empresaId, id)));
    }

    [SwaggerOperation(Summary = "Get supplier statistics (Operador only)", Description = "Returns average lead time, total purchases, on-time delivery rate.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/estatisticas")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetEstatisticas(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await obterEstatisticasUseCase.ExecuteAsync(new ObterEstatisticasFornecedorQuery(empresaId, id)));
    }

    [SwaggerOperation(Summary = "Create supplier (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Create([FromBody] CriarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/fornecedores/{resultado.Id}", resultado);
    }

    [SwaggerOperation(Summary = "Update supplier (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPatch("{id}")]
    [HttpPut("{id}")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();
        if (id != command.FornecedorId)
            return DataBadRequest("FornecedorId da rota difere do corpo.");

        await atualizarUseCase.ExecuteAsync(command);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Deactivate supplier (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarFornecedorCommand(id, empresaId));
        return NoContent();
    }
}
