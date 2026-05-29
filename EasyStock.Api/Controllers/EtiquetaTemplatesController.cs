using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Etiquetas;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Modelos de etiqueta (templates)")]
[ApiController]
[Route("api/etiquetas/templates")]
[Authorize]
[ValidateEmpresaId]
public class EtiquetaTemplatesController(
    ListarTemplatesUseCase listarUseCase,
    CriarTemplateUseCase criarUseCase,
    AtualizarTemplateUseCase atualizarUseCase,
    RemoverTemplateUseCase removerUseCase,
    DefinirPadraoUseCase definirPadraoUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List all templates (sistema + custom) for the empresa")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await listarUseCase.ExecuteAsync(new ListarTemplatesQuery(emp));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Create custom template")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Create([FromBody] CriarTemplateCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        try
        {
            var result = await criarUseCase.ExecuteAsync(command with
            {
                EmpresaId  = emp,
                OperadorId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
                Ip         = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent  = Request.Headers.UserAgent.ToString(),
            });
            return DataCreated($"/api/etiquetas/templates/{result.Id}", result);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Update custom template")]
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarTemplateCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        try
        {
            var result = await atualizarUseCase.ExecuteAsync(command with
            {
                EmpresaId  = emp,
                Id         = id,
                OperadorId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
                Ip         = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent  = Request.Headers.UserAgent.ToString(),
            });
            return result == null ? DataNotFound("Modelo não encontrado.") : DataOk(result);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (UseCaseConcurrencyException ex)
        {
            return Conflict(new { error = new { code = "CONCURRENCY_CONFLICT", message = ex.Message } });
        }
    }

    [SwaggerOperation(Summary = "Delete custom template (sistema returns 405)")]
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await removerUseCase.ExecuteAsync(new RemoverTemplateCommand(
            emp, id,
            currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()));
        if (!result.Removido) return DataNotFound("Modelo personalizado não encontrado.");
        return DataOk(new { removido = true, snapshotsExistentes = result.SnapshotsExistentes });
    }

    [SwaggerOperation(Summary = "Set default template for empresa")]
    [HttpPost("{origem}/{id:guid}/set-default")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> SetDefault(string origem, Guid id, [FromQuery] Guid? empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        try
        {
            var result = await definirPadraoUseCase.ExecuteAsync(new DefinirPadraoCommand(
                emp, origem, id,
                currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString()));
            return DataOk(result);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}
