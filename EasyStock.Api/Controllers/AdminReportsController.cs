using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Motor de relatórios assíncrono — Admin SaaS (cross-tenant).
/// Gate: policy "SuperAdmin". Todos os endpoints exigem header
/// <c>X-Admin-Motivo</c> com mínimo 10 caracteres (ADR-R12 + LGPD audit).
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Policy = "SuperAdmin")]
public sealed class AdminReportsController(
    EnqueueReportRunUseCase  enqueue,
    GetReportRunUseCase      getById,
    ListMyReportRunsUseCase  listMy,
    CancelReportRunUseCase   cancel,
    ListReportCatalogUseCase catalog,
    GetReportSchemaUseCase   schema,
    PreviewReportUseCase     preview,
    GetReportDataUseCase     data,
    IReportExecutionScope    executionScope,
    ICurrentUserAccessor     currentUser,
    ReportingMetricsService  reportingMetrics)
    : EasyStockControllerBase
{
    // ── GET /api/admin/reports/catalog ───────────────────────────────────────

    /// <summary>Lista os relatórios Admin SaaS disponíveis.</summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] ReportCategoria? categoria = null)
    {
        var result = await catalog.ExecuteAsync(
            new ListReportCatalogQuery(
                CategoriaFiltro: categoria));
        return DataOk(result);
    }

    // ── POST /api/admin/reports/{key}/runs ───────────────────────────────────

    /// <summary>
    /// Enfileira uma nova execução de relatório Admin.
    /// Header <c>X-Admin-Motivo</c> é obrigatório (mínimo 10 caracteres) — auditoria LGPD.
    /// </summary>
    [HttpPost("{key}/runs")]
    public async Task<IActionResult> EnqueueRun(
        [FromRoute] string           key,
        [FromBody]  AdminEnqueueRunRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        [FromHeader(Name = "X-Admin-Motivo")]  string? motivo = null)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            return DataBadRequest(
                "O header 'X-Admin-Motivo' é obrigatório e deve ter pelo menos 10 caracteres.");

        var command = new EnqueueReportRunCommand(
            ReportKey:      key,
            ParamsJson:     request.ParamsJson,
            Format:         request.Format,
            IdempotencyKey: idempotencyKey,
            MotivoExecucao: motivo.Trim());

        var result = await enqueue.ExecuteAsync(command);

        if (result.JaExistia)
            return DataOk(result.Run);

        return DataCreated(
            $"/api/admin/reports/runs/{result.Run.Id}",
            result.Run);
    }

    // ── GET /api/admin/reports/runs/{id} ─────────────────────────────────────

    /// <summary>Retorna o estado de uma execução Admin, incluindo <c>downloadUrl</c>.</summary>
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun([FromRoute] Guid id)
    {
        var run = await getById.ExecuteAsync(new GetReportRunQuery(id));
        if (run is null)
            return DataNotFound("Execução de relatório não encontrada.");

        return DataOk(run);
    }

    // ── GET /api/admin/reports/runs ──────────────────────────────────────────

    /// <summary>Lista execuções Admin — máx. 100 por página.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns(
        [FromQuery] ReportCategoria? categoria = null,
        [FromQuery] ReportStatus?    status    = null,
        [FromQuery] DateTimeOffset?  de        = null,
        [FromQuery] DateTimeOffset?  ate       = null,
        [FromQuery] int              skip      = 0,
        [FromQuery] int              take      = 25)
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

    // ── DELETE /api/admin/reports/runs/{id} ──────────────────────────────────

    /// <summary>Cancela uma execução Admin pendente ou em andamento.</summary>
    [HttpDelete("runs/{id:guid}")]
    public async Task<IActionResult> CancelRun([FromRoute] Guid id)
    {
        var found = await cancel.ExecuteAsync(new CancelReportRunCommand(id));
        if (!found)
            return DataNotFound("Execução de relatório não encontrada.");

        return NoContent();
    }

    // ── POST /api/admin/reports/runs/{id}/repeat ─────────────────────────────

    /// <summary>Reenfileira uma execução Admin com os mesmos parâmetros.</summary>
    [HttpPost("runs/{id:guid}/repeat")]
    public async Task<IActionResult> RepeatRun(
        [FromRoute] Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        [FromHeader(Name = "X-Admin-Motivo")]  string? motivo = null)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            return DataBadRequest(
                "O header 'X-Admin-Motivo' é obrigatório e deve ter pelo menos 10 caracteres.");

        var original = await getById.ExecuteAsync(new GetReportRunQuery(id));
        if (original is null)
            return DataNotFound("Execução de relatório não encontrada.");

        var command = new EnqueueReportRunCommand(
            ReportKey:      original.ReportKey,
            ParamsJson:     original.ParamsJson,
            Format:         original.Format,
            IdempotencyKey: idempotencyKey,
            MotivoExecucao: motivo.Trim());

        var result = await enqueue.ExecuteAsync(command);

        if (result.JaExistia)
            return DataOk(result.Run);

        return DataCreated(
            $"/api/admin/reports/runs/{result.Run.Id}",
            result.Run);
    }

    // ── GET /api/admin/reports/{key}/schema ──────────────────────────────────

    /// <summary>Retorna o JSON Schema dos parâmetros de um relatório Admin.</summary>
    [HttpGet("{key}/schema")]
    public async Task<IActionResult> GetSchema(
        [FromRoute] string key,
        CancellationToken  ct)
    {
        var result = await schema.ExecuteAsync(key, ct);
        if (result is null)
            return DataNotFound("Relatório não encontrado.");

        return DataOk(result);
    }

    // ── POST /api/admin/reports/{key}/preview ────────────────────────────────

    /// <summary>
    /// Pré-visualização síncrona de até 10 linhas (timeout 3s) — Admin SaaS.
    /// Contexto AdminSaaS: sem filtro de EmpresaId.
    /// </summary>
    [HttpPost("{key}/preview")]
    public async Task<IActionResult> Preview(
        [FromRoute] string              key,
        [FromBody]  AdminPreviewRequest request,
        CancellationToken               ct)
    {
        using var _ = executionScope.Begin(
            empresaId:            null,
            usuarioSolicitanteId: currentUser.UsuarioId,
            contexto:             ReportContexto.AdminSaaS);

        reportingMetrics.RecordPreviewRequested(key);

        var result = await preview.ExecuteAsync(
            new PreviewReportQuery(key, request.ParamsJson), ct);

        if (result is null)
            return DataNotFound("Relatório não encontrado.");

        if (result is { Available: false, Reason: "TooSlow" })
            reportingMetrics.RecordPreviewTimedOut(key);

        return DataOk(result);
    }

    // ── POST /api/admin/reports/{key}/data ───────────────────────────────────

    /// <summary>
    /// Endpoint síncrono paginado para IA / Power BI — Admin SaaS.
    /// Requer header <c>X-Admin-Motivo</c> (LGPD audit).
    /// </summary>
    [HttpPost("{key}/data")]
    public async Task<IActionResult> GetData(
        [FromRoute] string            key,
        [FromBody]  AdminDataRequest  request,
        [FromHeader(Name = "X-Admin-Motivo")] string? motivo = null,
        CancellationToken             ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            return DataBadRequest(
                "O header 'X-Admin-Motivo' é obrigatório e deve ter pelo menos 10 caracteres.");

        using var _ = executionScope.Begin(
            empresaId:            null,
            usuarioSolicitanteId: currentUser.UsuarioId,
            contexto:             ReportContexto.AdminSaaS);

        reportingMetrics.RecordDataRequested("admin", key);

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

// ── Request DTOs Admin ───────────────────────────────────────────────────────

/// <summary>Body do POST /api/admin/reports/{key}/runs</summary>
public sealed record AdminEnqueueRunRequest(
    string       ParamsJson,
    ReportFormat Format);

/// <summary>Body do POST /api/admin/reports/{key}/preview</summary>
public sealed record AdminPreviewRequest(string ParamsJson);

/// <summary>Body do POST /api/admin/reports/{key}/data</summary>
public sealed record AdminDataRequest(
    string ParamsJson,
    int    Page     = 1,
    int    PageSize = 50);
