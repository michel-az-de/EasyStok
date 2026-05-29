using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Categorias;
using EasyStock.Domain.Enums.Financeiro;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Categorias Financeiras (CAP/CAR)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/financeiro/categorias")]
public class CategoriasFinanceirasController(
    CriarCategoriaFinanceiraUseCase criarUseCase,
    AtualizarCategoriaFinanceiraUseCase atualizarUseCase,
    InativarCategoriaFinanceiraUseCase inativarUseCase,
    ReativarCategoriaFinanceiraUseCase reativarUseCase,
    ListarCategoriasFinanceirasUseCase listarUseCase,
    MoverCategoriaFinanceiraUseCase moverUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? ativa,
        [FromQuery] TipoCategoriaFinanceira? tipo,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var itens = await listarUseCase.ExecuteAsync(new ListarCategoriasFinanceirasQuery(eid, ativa, tipo), ct);
        return DataOk(itens);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await criarUseCase.ExecuteAsync(cmd with { EmpresaId = eid }, ct);
            return DataCreated($"/api/financeiro/categorias/{r.Id}", r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
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
            var ok = await inativarUseCase.ExecuteAsync(new InativarCategoriaFinanceiraCommand(eid, id), ct);
            return ok ? NoContent() : DataNotFound();
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var ok = await reativarUseCase.ExecuteAsync(new ReativarCategoriaFinanceiraCommand(eid, id), ct);
        return ok ? NoContent() : DataNotFound();
    }

    [HttpPost("{id:guid}/mover")]
    public async Task<IActionResult> Mover(Guid id, [FromBody] MoverCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        if (id != cmd.Id) return DataBadRequest("Id da rota difere do corpo.");
        try
        {
            var r = await moverUseCase.ExecuteAsync(cmd with { EmpresaId = eid, Id = id }, ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
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
        if (!currentUser.TemPermissao(Permissao.GerenciarCategoriasFinanceiras))
        {
            error = Forbid();
            return false;
        }
        error = null;
        return true;
    }
}
