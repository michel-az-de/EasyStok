using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;
using EasyStock.Api.Http;
using EasyStock.Infra.Async.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Motor de relatórios assíncrono — Fase 1 (Tenant context).
/// Endpoints Admin em <see cref="AdminReportsController"/> (rota /api/admin/reports).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(
    EnqueueReportRunUseCase     enqueue,
    GetReportRunUseCase         getById,
    ListMyReportRunsUseCase     listMy,
    CancelReportRunUseCase      cancel,
    ListReportCatalogUseCase    catalog,
    GetReportSchemaUseCase      schema,
    PreviewReportUseCase        preview,
    GetReportDataUseCase        data,
    IReportExecutionScope       executionScope,
    ICurrentUserAccessor        currentUser,
    ReportingMetricsService     reportingMetrics)
    : EasyStockControllerBase
{
    // ── GET /api/reports/catalog ─────────────────────────────────────────────

    /// <summary>Lista os relatórios disponíveis para o usuário autenticado.</summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] ReportCategoria? categoria = null)
    {
        var result = await catalog.ExecuteAsync(
            new ListReportCatalogQuery(CategoriaFiltro: categoria));
        return DataOk(result);
    }

    // ── POST /api/reports/{key}/runs ─────────────────────────────────────────

    /// <summary>
    /// Enfileira uma nova execução de relatório.
    /// O header <c>Idempotency-Key</c> (opcional) habilita idempotência forte.
    /// </summary>
    [HttpPost("{key}/runs")]
    public async Task<IActionResult> EnqueueRun(
        [FromRoute] string           key,
        [FromBody]  EnqueueRunRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        var command = new EnqueueReportRunCommand(
            ReportKey:      key,
            ParamsJson:     request.ParamsJson,
            Format:         request.Format,
            IdempotencyKey: idempotencyKey,
            MotivoExecucao: null);

        var result = await enqueue.ExecuteAsync(command);

        if (result.JaExistia)
        {
            // 200 (não 202) para indicar dedup — mesmo conteúdo
            return DataOk(result.Run);
        }

        return DataCreated(
            $"/api/reports/runs/{result.Run.Id}",
            result.Run);
    }

    // ── GET /api/reports/runs/{id} ───────────────────────────────────────────

    /// <summary>
    /// Retorna o estado de uma execução, incluindo <c>downloadUrl</c> pré-assinada
    /// (TTL 15 min) quando o artefato estiver disponível.
    /// </summary>
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun([FromRoute] Guid id)
    {
        var run = await getById.ExecuteAsync(new GetReportRunQuery(id));
        if (run is null)
            return DataNotFound("Execução de relatório não encontrada.");

        return DataOk(run);
    }

    // ── GET /api/reports/runs ────────────────────────────────────────────────

    /// <summary>
    /// Lista execuções do usuário autenticado — máx. 100 por página.
    /// Filtros: categoria, status, de, ate (geração), skip, take.
    /// </summary>
    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns(
        [FromQuery] ReportCategoria?  categoria = null,
        [FromQuery] ReportStatus?     status    = null,
        [FromQuery] DateTimeOffset?   de        = null,
        [FromQuery] DateTimeOffset?   ate       = null,
        [FromQuery] int               skip      = 0,
        [FromQuery] int               take      = 25)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);

        var filter = new ReportListFilter(
            Categoria: categoria,
            Status:    status,
            De:        de,
            Ate:       ate);

        var runs = await listMy.ExecuteAsync(
            new ListMyReportRunsQuery(Filter: filter, Skip: skip, Take: take));

        return DataOk(runs);
    }

    // ── DELETE /api/reports/runs/{id} ────────────────────────────────────────

    /// <summary>
    /// Cancela uma execução pendente ou em andamento.
    /// Retorna 204 independente de o cancelamento ser imediato ou assíncrono (Canceling).
    /// </summary>
    [HttpDelete("runs/{id:guid}")]
    public async Task<IActionResult> CancelRun([FromRoute] Guid id)
    {
        var found = await cancel.ExecuteAsync(new CancelReportRunCommand(id));
        if (!found)
            return DataNotFound("Execução de relatório não encontrada.");

        return NoContent();
    }

    // ── POST /api/reports/runs/{id}/repeat ──────────────────────────────────

    /// <summary>
    /// Reenfileira uma execução existente com os mesmos parâmetros.
    /// Útil para "Tentar de novo" ou "Gerar novamente" após sucesso.
    /// </summary>
    [HttpPost("runs/{id:guid}/repeat")]
    public async Task<IActionResult> RepeatRun(
        [FromRoute] Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        var original = await getById.ExecuteAsync(new GetReportRunQuery(id));
        if (original is null)
            return DataNotFound("Execução de relatório não encontrada.");

        var command = new EnqueueReportRunCommand(
            ReportKey:      original.ReportKey,
            ParamsJson:     original.ParamsJson,
            Format:         original.Format,
            IdempotencyKey: idempotencyKey,
            MotivoExecucao: null);

        var result = await enqueue.ExecuteAsync(command);

        if (result.JaExistia)
            return DataOk(result.Run);

        return DataCreated(
            $"/api/reports/runs/{result.Run.Id}",
            result.Run);
    }

    // ── GET /api/reports/{key}/schema ────────────────────────────────────────

    /// <summary>
    /// Retorna o JSON Schema (draft-07) dos parâmetros de um relatório.
    /// Gerado via reflexão sobre <see cref="IReportDefinition.ParamsType"/>.
    /// Usado pelo front-end para montar formulário dinâmico de parametrização.
    /// </summary>
    [HttpGet("{key}/schema")]
    public async Task<IActionResult> GetSchema(
        [FromRoute] string key,
        CancellationToken ct)
    {
        var result = await schema.ExecuteAsync(key, ct);
        if (result is null)
            return DataNotFound("Relatório não encontrado.");

        return DataOk(result);
    }

    // ── POST /api/reports/{key}/preview ──────────────────────────────────────

    /// <summary>
    /// Pré-visualização síncrona de até 10 linhas (timeout 3s).
    /// Não persiste <c>ReportRun</c>. Útil para validar parâmetros antes de enfileirar.
    /// </summary>
    [HttpPost("{key}/preview")]
    public async Task<IActionResult> Preview(
        [FromRoute] string         key,
        [FromBody]  PreviewRequest request,
        CancellationToken          ct)
    {
        using var _ = executionScope.Begin(
            empresaId:            currentUser.EmpresaId,
            usuarioSolicitanteId: currentUser.UsuarioId,
            contexto:             Domain.Reporting.ReportContexto.Tenant);

        reportingMetrics.RecordPreviewRequested(key);

        var result = await preview.ExecuteAsync(
            new PreviewReportQuery(key, request.ParamsJson), ct);

        if (result is null)
            return DataNotFound("Relatório não encontrado.");

        if (result is { Available: false, Reason: "TooSlow" })
            reportingMetrics.RecordPreviewTimedOut(key);

        return DataOk(result);
    }

    // ── POST /api/reports/{key}/data ─────────────────────────────────────────

    /// <summary>
    /// Endpoint síncrono paginado para consumo por IA / Power BI / MCP — ADR-R15.
    /// Não persiste <c>ReportRun</c>. Máx. pageSize = 200.
    /// Registra acesso em AuditLog para rastreabilidade LGPD.
    /// </summary>
    [HttpPost("{key}/data")]
    public async Task<IActionResult> GetData(
        [FromRoute] string       key,
        [FromBody]  DataRequest  request,
        CancellationToken        ct)
    {
        using var _ = executionScope.Begin(
            empresaId:            currentUser.EmpresaId,
            usuarioSolicitanteId: currentUser.UsuarioId,
            contexto:             Domain.Reporting.ReportContexto.Tenant);

        reportingMetrics.RecordDataRequested("tenant", key);

        var query = new GetReportDataQuery(
            ReportKey:  key,
            ParamsJson: request.ParamsJson,
            Page:       request.Page,
            PageSize:   request.PageSize);

        var result = await data.ExecuteAsync(query, ct);

        if (result is null)
            return DataNotFound("Relatório não encontrado.");

        return DataOk(result);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>Body do POST /api/reports/{key}/runs</summary>
public sealed record EnqueueRunRequest(
    string       ParamsJson,
    ReportFormat Format);

/// <summary>Body do POST /api/reports/{key}/preview</summary>
public sealed record PreviewRequest(string ParamsJson);

/// <summary>Body do POST /api/reports/{key}/data</summary>
public sealed record DataRequest(
    string ParamsJson,
    int    Page     = 1,
    int    PageSize = 50);
