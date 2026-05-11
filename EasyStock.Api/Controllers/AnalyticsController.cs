using EasyStock.Api.Configuration;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using EasyStock.Application.UseCases.Analytics.Alertas;
using EasyStock.Application.UseCases.Analytics.Dashboard;
using EasyStock.Application.UseCases.Analytics.Dia;
using EasyStock.Application.UseCases.Analytics.Margem;
using EasyStock.Application.UseCases.Analytics.Movimentacoes;
using EasyStock.Application.UseCases.Analytics.Parados;
using EasyStock.Application.UseCases.Analytics.Projecoes;
using EasyStock.Application.UseCases.Analytics.Receita;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Application.UseCases.Analytics.Sazonalidade;
using EasyStock.Application.UseCases.Analytics.Validade;
using EasyStock.Application.UseCases.Analytics.VendasPorCanal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Analytics")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/analytics")]
[EnableRateLimiting("geral")]
public class AnalyticsController(
    GetDashboardUseCase getDashboardUseCase,
    ObterResumoDiaUseCase obterResumoDiaUseCase,
    CalcularProjecoesUseCase calcProjecoesUseCase,
    CalcularReposicaoUseCase calcReposicaoUseCase,
    CalcularSazonalidadeUseCase calcSazonalidadeUseCase,
    ObterAlertasUseCase obterAlertasUseCase,
    ObterDiasAlertaValidadeUseCase obterDiasAlertaValidadeUseCase,
    CalcularReceitaUseCase calcReceitaUseCase,
    CalcularMargemUseCase calcMargemUseCase,
    ObterMovimentacoesUseCase obterMovimentacoesUseCase,
    ObterValidadeUseCase obterValidadeUseCase,
    ObterParadosUseCase obterParadosUseCase,
    ObterDiasAlertaParadoUseCase obterDiasAlertaParadoUseCase,
    ObterVendasPorCanalUseCase obterVendasPorCanalUseCase,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
{

    [SwaggerOperation(Summary = "Get dashboard summary", Description = "Aggregated KPIs: total products, stock items, low-stock count, expiring items, monthly revenue.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await getDashboardUseCase.ExecuteAsync(new GetDashboardCommand(resolvedEmpresaId, periodo));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Resumo do dia em curso", Description = "Vendas, pedidos pendentes, status do caixa e Pix recebidos no dia. Usado pelo dashboard primario.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("dia")]
    public async Task<IActionResult> Dia([FromQuery] Guid empresaId, [FromQuery] Guid? lojaId = null)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await obterResumoDiaUseCase.ExecuteAsync(new ObterResumoDiaCommand(resolvedEmpresaId, lojaId));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get stock rupture projections (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("projecoes")]
    public async Task<IActionResult> Projecoes(
        [FromQuery] Guid empresaId,
        [FromQuery] int diasHistorico = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await calcProjecoesUseCase.ExecuteAsync(
            new CalcularProjecoesCommand(resolvedEmpresaId, diasHistorico, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get replenishment suggestions (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("reposicao")]
    public async Task<IActionResult> Reposicao(
        [FromQuery] Guid empresaId,
        [FromQuery] int diasHistorico = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await calcReposicaoUseCase.ExecuteAsync(
            new CalcularReposicaoCommand(resolvedEmpresaId, diasHistorico, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get product seasonality analysis")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("sazonalidade")]
    public async Task<IActionResult> Sazonalidade([FromQuery] Guid empresaId, [FromQuery] Guid produtoId, [FromQuery] int meses = 12)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await calcSazonalidadeUseCase.ExecuteAsync(
            new CalcularSazonalidadeCommand(resolvedEmpresaId, produtoId, meses));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get stock alerts (paginated)", Description = "Returns low-stock and expiry alerts within a time window.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? dias = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var diasEfetivos = dias;
        if (!diasEfetivos.HasValue)
        {
            var daysResult = await obterDiasAlertaValidadeUseCase.ExecuteAsync(new ObterDiasAlertaValidadeCommand(lojaId));
            diasEfetivos = daysResult.Dias;
        }

        var (items, totalCount) = await obterAlertasUseCase.ExecuteAsync(
            new ObterAlertasCommand(resolvedEmpresaId, lojaId, diasEfetivos, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get revenue by month")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("receita")]
    public async Task<IActionResult> Receita([FromQuery] Guid empresaId, [FromQuery] int meses = 12)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await calcReceitaUseCase.ExecuteAsync(new CalcularReceitaCommand(resolvedEmpresaId, meses));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get product margin analysis (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("margem")]
    public async Task<IActionResult> Margem(
        [FromQuery] Guid empresaId,
        [FromQuery] int dias = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (p, ps) = NormalisePage(page, pageSize);
        var (result, total) = await calcMargemUseCase.ExecuteAsync(
            new CalcularMargemCommand(resolvedEmpresaId, dias, p, ps));
        return DataPaged(result, total, p, ps);
    }

    [SwaggerOperation(Summary = "Get movement analytics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("movimentacoes")]
    public async Task<IActionResult> Movimentacoes(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] EasyStock.Domain.Enums.TipoMovimentacaoEstoque? tipo,
        [FromQuery] int diasPadrao = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await obterMovimentacoesUseCase.ExecuteAsync(
            new ObterMovimentacoesCommand(resolvedEmpresaId, de, ate, tipo, diasPadrao));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get items near expiry (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("validade")]
    public async Task<IActionResult> Validade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? dias = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var diasEfetivos = dias;
        if (!diasEfetivos.HasValue)
        {
            var daysResult = await obterDiasAlertaValidadeUseCase.ExecuteAsync(new ObterDiasAlertaValidadeCommand(lojaId));
            diasEfetivos = daysResult.Dias;
        }

        var (items, totalCount) = await obterValidadeUseCase.ExecuteAsync(
            new ObterValidadeCommand(resolvedEmpresaId, lojaId, diasEfetivos, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get slow-moving items (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("parados")]
    public async Task<IActionResult> Parados(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? diasSemMovimento = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var diasEfetivos = diasSemMovimento;
        if (!diasEfetivos.HasValue)
        {
            var daysResult = await obterDiasAlertaParadoUseCase.ExecuteAsync(new ObterDiasAlertaParadoCommand(lojaId));
            diasEfetivos = daysResult.Dias;
        }

        var (items, totalCount) = await obterParadosUseCase.ExecuteAsync(
            new ObterParadosCommand(resolvedEmpresaId, lojaId, diasEfetivos, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get sales by channel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("vendas-por-canal")]
    public async Task<IActionResult> VendasPorCanal(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int diasPadrao = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var result = await obterVendasPorCanalUseCase.ExecuteAsync(
            new ObterVendasPorCanalCommand(resolvedEmpresaId, de, ate, diasPadrao));
        return DataOk(result);
    }
}
