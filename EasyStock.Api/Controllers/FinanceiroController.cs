using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Dashboard;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Financeiro - Dashboard e Fluxo de Caixa")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/financeiro")]
public class FinanceiroController(
    ObterDashboardFinanceiroUseCase dashboardUseCase,
    ObterFluxoCaixaUseCase fluxoUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var r = await dashboardUseCase.ExecuteAsync(new ObterDashboardFinanceiroQuery(eid), ct);
        return DataOk(r);
    }

    [HttpGet("fluxo-caixa")]
    public async Task<IActionResult> FluxoCaixa(
        [FromQuery] Guid? empresaId,
        [FromQuery] PeriodicidadeFluxo periodicidade = PeriodicidadeFluxo.Mensal,
        [FromQuery] DateTime? inicio = null,
        [FromQuery] DateTime? fim = null,
        [FromQuery] Guid? categoriaId = null,
        [FromQuery] Guid? centroCustoId = null,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var iniDef = inicio ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fimDef = fim ?? iniDef.AddMonths(6).AddSeconds(-1);

        try
        {
            var r = await fluxoUseCase.ExecuteAsync(new ObterFluxoCaixaQuery(
                eid, periodicidade, iniDef, fimDef, categoriaId, centroCustoId), ct);
            return DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
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
}
