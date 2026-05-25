using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class RelatoriosService(ApiClient api)
{
    public Task<ApiResult<List<ReportCatalogItemApi>>> CatalogAsync(string? categoria = null)
    {
        var qs = "reports/catalog";
        if (!string.IsNullOrEmpty(categoria)) qs += $"?categoria={Uri.EscapeDataString(categoria)}";
        return api.GetAsync<List<ReportCatalogItemApi>>(qs);
    }

    public Task<ApiResult<ReportRunApi>> EnqueueAsync(string key, string paramsJson, string format)
        => api.PostAsync<ReportRunApi>($"reports/{Uri.EscapeDataString(key)}/runs", new
        {
            paramsJson,
            format
        });

    public Task<ApiResult<ReportRunApi>> GetRunAsync(Guid id)
        => api.GetAsync<ReportRunApi>($"reports/runs/{id}");

    public Task<ApiResult<ReportRunListApi>> ListRunsAsync(
        string? categoria = null, string? status = null, int skip = 0, int take = 25)
    {
        var qs = $"reports/runs?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(categoria)) qs += $"&categoria={Uri.EscapeDataString(categoria)}";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        return api.GetAsync<ReportRunListApi>(qs);
    }

    public Task<ApiResult<bool>> CancelRunAsync(Guid id)
        => api.DeleteAsync($"reports/runs/{id}");

    public Task<ApiResult<ReportRunApi>> RepeatRunAsync(Guid id)
        => api.PostAsync<ReportRunApi>($"reports/runs/{id}/repeat", new { });

    public Task<ApiResult<ReportSchemaApi>> GetSchemaAsync(string key)
        => api.GetAsync<ReportSchemaApi>($"reports/{Uri.EscapeDataString(key)}/schema");

    public Task<ApiResult<ReportPreviewApi>> PreviewAsync(string key, string paramsJson)
        => api.PostAsync<ReportPreviewApi>($"reports/{Uri.EscapeDataString(key)}/preview", new { paramsJson });
}

// ── API DTOs ────────────────────────────────────────────────────────────────

public record ReportCatalogItemApi(
    string Key,
    string Label,
    string Descricao,
    string Categoria,
    string Contexto,
    string IconKey,
    List<string> FormatosSuportados,
    string PermissaoRequerida,
    string SemanticVersion,
    string SchemaUrl);

public record ReportRunApi(
    Guid Id,
    string ReportKey,
    string ReportLabel,
    string Categoria,
    string Contexto,
    string Format,
    string Status,
    string StatusLabel,
    string ParamsJson,
    int Tentativas,
    int MaxTentativas,
    long? RowCount,
    long? ArtifactSizeBytes,
    string? ErrorMessage,
    string? WarningsJson,
    string? SemanticVersion,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ExpiresAt,
    string? DownloadUrl,
    DateTimeOffset? DownloadExpiresAt,
    bool CanDownload);

public record ReportRunListApi(
    List<ReportRunApi> Items,
    int Total);

public record ReportSchemaApi(
    string Key,
    string Label,
    object Schema);

public record ReportPreviewApi(
    bool Available,
    string? Reason,
    List<object>? Rows,
    List<string>? Columns,
    int? TotalEstimated);
