using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.CentrosCusto;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Centros de Custo (CAP/CAR)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/financeiro/centros-custo")]
public class CentrosCustoController(
    CriarCentroCustoUseCase criarUseCase,
    AtualizarCentroCustoUseCase atualizarUseCase,
    InativarCentroCustoUseCase inativarUseCase,
    ReativarCentroCustoUseCase reativarUseCase,
    ListarCentrosCustoUseCase listarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? ativo,
        [FromQuery] Guid? lojaId,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var itens = await listarUseCase.ExecuteAsync(new ListarCentrosCustoQuery(eid, ativo, lojaId), ct);
        return DataOk(itens);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await criarUseCase.ExecuteAsync(cmd with { EmpresaId = eid }, ct);
            return DataCreated($"/api/financeiro/centros-custo/{r.Id}", r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        if (id != cmd.Id) return DataBadRequest("Id da rota difere do corpo.");
        try
        {
            var r = await atualizarUseCase.ExecuteAsync(cmd with { EmpresaId = eid, Id = id }, ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/inativar")]
    public async Task<IActionResult> Inativar(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        try
        {
            var ok = await inativarUseCase.ExecuteAsync(new InativarCentroCustoCommand(eid, id), ct);
            return ok ? NoContent() : DataNotFound();
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var ok = await reativarUseCase.ExecuteAsync(new ReativarCentroCustoCommand(eid, id), ct);
        return ok ? NoContent() : DataNotFound();
    }

    private bool RequerVisualizar(out IActionResult? error)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarContasAPagar) &&
            !currentUser.TemPermissao(Permissao.VisualizarContasAReceber))
        {
            error = Forbid();
            return false;
        }
        error = null;
        return true;
    }

    private bool RequerGerenciar(out IActionResult? error)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarCentrosCusto))
        {
            error = Forbid();
            return false;
        }
        error = null;
        return true;
    }
}
