using System.Reflection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.UseCases.Reports;

/// <summary>
/// Executa uma pré-visualização síncrona de até 10 linhas de um relatório (timeout 3 s).
/// Não persiste <see cref="ReportRun"/>. Usada pelo endpoint <c>POST /api/reports/{key}/preview</c>.
/// Requer que <see cref="IReportExecutionScope"/> esteja inicializado ANTES de chamar ExecuteAsync.
/// </summary>
public sealed class PreviewReportUseCase(
    ReportRegistry registry,
    IServiceProvider serviceProvider)
{
    // Reflection handle para o wrapper genérico — evita CS8416 (não é possível await foreach em dynamic)
    private static readonly MethodInfo s_wrapMethod =
        typeof(PreviewReportUseCase)
            .GetMethod(nameof(WrapAsObjectStream), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<PreviewResult?> ExecuteAsync(
        PreviewReportQuery query, CancellationToken ct)
    {
        var definition = registry.Find(query.ReportKey);
        if (definition is null)
            return null;

        // Dynamic dispatch — resolve handler pelo tipo concreto de TParams/TRow
        var handlerType = typeof(IReportHandler<,>)
            .MakeGenericType(definition.ParamsType, definition.RowType);

        dynamic handler = serviceProvider.GetRequiredService(handlerType);
        dynamic paramsObj = handler.DeserializeParams(query.ParamsJson);

        // Timeout 3 s — se demorar, retorna Available=false
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        var rows = new List<Dictionary<string, object?>>();
        var timedOut = false;

        try
        {
            // Obtém IAsyncEnumerable<TRow> como object e converte para IAsyncEnumerable<object>
            // via método genérico chamado por reflection — evita await foreach em dynamic (CS8416).
            var rawStream = (object)handler.StreamAsync(paramsObj, timeoutCts.Token);
            var wrapGeneric = s_wrapMethod.MakeGenericMethod(definition.RowType);
            var rowsEnumerable = (IAsyncEnumerable<object>)wrapGeneric.Invoke(null, [rawStream])!;

            await foreach (var row in rowsEnumerable
                .WithCancellation(timeoutCts.Token)
                .ConfigureAwait(false))
            {
                rows.Add(SerializeRow(row, definition.RowType));
                if (rows.Count >= 10) break;
            }
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            timedOut = true;
        }

        if (timedOut)
            return new PreviewResult(Available: false, Rows: [], Reason: "TooSlow");

        return new PreviewResult(Available: true, Rows: rows, Reason: null);
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

/// <summary>Query para pré-visualização de relatório.</summary>
public sealed record PreviewReportQuery(
    string ReportKey,
    string ParamsJson);

/// <summary>Resultado da pré-visualização.</summary>
public sealed record PreviewResult(
    bool Available,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string? Reason);
