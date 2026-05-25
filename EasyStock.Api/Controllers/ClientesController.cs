using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AdicionarClienteDocumento;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.AdicionarClienteTelefone;
using EasyStock.Application.UseCases.AtualizarCliente;
using EasyStock.Application.UseCases.BuscarCliente;
using EasyStock.Application.UseCases.CriarCliente;
using EasyStock.Application.UseCases.DesativarCliente;
using EasyStock.Application.UseCases.ListarClientes;
using EasyStock.Application.UseCases.ObterClienteDetalhes;
using EasyStock.Application.UseCases.ReativarCliente;
using EasyStock.Application.UseCases.RemoverClienteDocumento;
using EasyStock.Application.UseCases.RemoverClienteEndereco;
using EasyStock.Application.UseCases.RemoverClienteTelefone;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Clientes")]
[ApiController]
[Route("api/clientes")]
[Authorize]
[ValidateEmpresaId]
public class ClientesController(
    CriarClienteUseCase criarUseCase,
    AtualizarClienteUseCase atualizarUseCase,
    ListarClientesUseCase listarUseCase,
    BuscarClienteUseCase buscarUseCase,
    ObterClienteDetalhesUseCase obterDetalhesUseCase,
    DesativarClienteUseCase desativarUseCase,
    ReativarClienteUseCase reativarUseCase,
    AdicionarClienteEnderecoUseCase addEnderecoUseCase,
    RemoverClienteEnderecoUseCase removeEnderecoUseCase,
    AdicionarClienteTelefoneUseCase addTelefoneUseCase,
    RemoverClienteTelefoneUseCase removeTelefoneUseCase,
    AdicionarClienteDocumentoUseCase addDocumentoUseCase,
    RemoverClienteDocumentoUseCase removeDocumentoUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    // ── CRUD raiz ──────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List clients (paginated)")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? ativo = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "nome",
        [FromQuery] string? order = "asc")
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarUseCase.ExecuteAsync(
            new ListarClientesQuery(emp, p, sz, ativo, search, sort, NormaliseOrder(order)));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Quick search clients (autocomplete)")]
    [HttpGet("buscar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Buscar(
        [FromQuery] string termo,
        [FromQuery] Guid? empresaId,
        [FromQuery] int max = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        return DataOk(await buscarUseCase.ExecuteAsync(new BuscarClienteQuery(emp, termo ?? "", max)));
    }

    [SwaggerOperation(Summary = "Get client details (with sub-collections)")]
    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await obterDetalhesUseCase.ExecuteAsync(new ObterClienteDetalhesQuery(emp, id));
        return result == null ? DataNotFound("Cliente não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Create client")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Create([FromBody] CriarClienteCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await criarUseCase.ExecuteAsync(command with { EmpresaId = emp });
        return DataCreated($"/api/clientes/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Update client")]
    [HttpPatch("{id}")]
    [HttpPut("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarClienteCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        if (id != command.Id) return DataBadRequest("Id da rota difere do corpo.");

        var result = await atualizarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            Id = id,
            AlteradoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return result == null ? DataNotFound("Cliente não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "Deactivate client")]
    [HttpDelete("{id}")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await desativarUseCase.ExecuteAsync(new DesativarClienteCommand(
            emp, id,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            null, "web"));
        return ok ? NoContent() : DataNotFound("Cliente não encontrado.");
    }

    [SwaggerOperation(Summary = "Reactivate client")]
    [HttpPost("{id}/reativar")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Reativar(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await reativarUseCase.ExecuteAsync(new ReativarClienteCommand(
            emp, id,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            null, "web"));
        return ok ? DataOk(new { reativado = true }) : DataNotFound("Cliente não encontrado.");
    }

    // ── Endereços ──────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Add address to client")]
    [HttpPost("{id}/enderecos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddEndereco(Guid id, [FromBody] AdicionarClienteEnderecoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var newId = await addEnderecoUseCase.ExecuteAsync(command with { EmpresaId = emp, ClienteId = id });
        return newId == null ? DataNotFound("Cliente não encontrado.") : DataCreated($"/api/clientes/{id}/enderecos/{newId}", new { id = newId });
    }

    [SwaggerOperation(Summary = "Remove address")]
    [HttpDelete("{id}/enderecos/{enderecoId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveEndereco(Guid id, Guid enderecoId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await removeEnderecoUseCase.ExecuteAsync(new RemoverClienteEnderecoCommand(emp, id, enderecoId));
        return ok ? NoContent() : DataNotFound();
    }

    // ── Telefones ──────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Add phone to client")]
    [HttpPost("{id}/telefones")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddTelefone(Guid id, [FromBody] AdicionarClienteTelefoneCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var newId = await addTelefoneUseCase.ExecuteAsync(command with { EmpresaId = emp, ClienteId = id });
        return newId == null ? DataNotFound("Cliente não encontrado.") : DataCreated($"/api/clientes/{id}/telefones/{newId}", new { id = newId });
    }

    [SwaggerOperation(Summary = "Remove phone")]
    [HttpDelete("{id}/telefones/{telefoneId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveTelefone(Guid id, Guid telefoneId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await removeTelefoneUseCase.ExecuteAsync(new RemoverClienteTelefoneCommand(emp, id, telefoneId));
        return ok ? NoContent() : DataNotFound();
    }

    // ── Documentos ─────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Add document to client")]
    [HttpPost("{id}/documentos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> AddDocumento(Guid id, [FromBody] AdicionarClienteDocumentoCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var newId = await addDocumentoUseCase.ExecuteAsync(command with { EmpresaId = emp, ClienteId = id });
        return newId == null ? DataNotFound("Cliente não encontrado.") : DataCreated($"/api/clientes/{id}/documentos/{newId}", new { id = newId });
    }

    [SwaggerOperation(Summary = "Remove document")]
    [HttpDelete("{id}/documentos/{documentoId}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RemoveDocumento(Guid id, Guid documentoId, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var ok = await removeDocumentoUseCase.ExecuteAsync(new RemoverClienteDocumentoCommand(emp, id, documentoId));
        return ok ? NoContent() : DataNotFound();
    }
}
