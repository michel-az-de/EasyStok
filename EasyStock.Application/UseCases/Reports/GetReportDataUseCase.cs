using System.Reflection;
using EasyStock.Application.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.UseCases.Reports;

/// <summary>
/// Endpoint síncrono paginado <c>POST /api/reports/{key}/data</c>.
/// Hook para IA / Power BI / MCP — ADR-R15.
/// Não persiste <see cref="Domain.Reporting.ReportRun"/>.
/// Máx. pageSize = 200. Requer <see cref="IReportExecutionScope"/> inicializado.
/// </summary>
public sealed class GetReportDataUseCase(
    ReportRegistry registry,
    IServiceProvider serviceProvider)
{
    public const int MaxPageSize = 200;

    // Reflection handle para o wrapper genérico — evita CS8416 (não é possível await foreach em dynamic)
    private static readonly MethodInfo s_wrapMethod =
        typeof(GetReportDataUseCase)
            .GetMethod(nameof(WrapAsObjectStream), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<ReportDataResult?> ExecuteAsync(
        GetReportDataQuery query, CancellationToken ct)
    {
        var definition = registry.Find(query.ReportKey);
        if (definition is null)
            return null;

        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        int page = Math.Max(1, query.Page);
        int skip = (page - 1) * pageSize;

        var handlerType = typeof(IReportHandler<,>)
            .MakeGenericType(definition.ParamsType, definition.RowType);

        dynamic handler = serviceProvider.GetRequiredService(handlerType);
        dynamic paramsObj = handler.DeserializeParams(query.ParamsJson);

        var rows = new List<Dictionary<string, object?>>();
        int rowIndex = 0;

        // Obtém IAsyncEnumerable<TRow> como object e converte via reflection — evita CS8416
        var rawStream = (object)handler.StreamAsync(paramsObj, ct);
        var wrapGeneric = s_wrapMethod.MakeGenericMethod(definition.RowType);
        var rowsEnumerable = (IAsyncEnumerable<object>)wrapGeneric.Invoke(null, [rawStream])!;

        await foreach (var row in rowsEnumerable.WithCancellation(ct).ConfigureAwait(false))
        {
            if (rowIndex < skip)
            {
                rowIndex++;
                continue;
            }

            rows.Add(SerializeRow(row, definition.RowType));

            if (rows.Count >= pageSize + 1) // +1 to detect hasMore
                break;

            rowIndex++;
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new ReportDataResult(
            Rows: rows,
            Page: page,
            PageSize: pageSize,
            HasMore: hasMore);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Wrapper genérico tipado para contornar CS8416 — chamado via reflection em runtime.
    /// Converte <see cref="IAsyncEnumerable{TRow}"/> → <see cref="IAsyncEnumerable{object}"/>.
    /// </summary>
    private static async IAsyncEnumerable<object> WrapAsObjectStream<TRow>(
        IAsyncEnumerable<TRow> source)
    {
        await foreach (var item in source)
            yield return item!;
    }

    private static Dictionary<string, object?> SerializeRow(object row, Type rowType)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in rowType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            var camel = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
            result[camel] = prop.GetValue(row);
        }
        return result;
    }
}

/// <summary>Query para o endpoint síncrono paginado.</summary>
public sealed record GetReportDataQuery(
    string ReportKey,
    string ParamsJson,
    int Page = 1,
    int PageSize = 50,
    string? MotivoAdmin = null);

/// <summary>Resposta paginada do endpoint /data.</summary>
public sealed record ReportDataResult(
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int Page,
    int PageSize,
    bool HasMore);
