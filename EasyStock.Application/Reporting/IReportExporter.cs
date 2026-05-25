using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Contrato de serialização de linhas de dados para um formato de arquivo.
/// </summary>
public interface IReportExporter
{
    ReportFormat Format { get; }
    string ContentType { get; }
    string FileExtension { get; }

    Task WriteAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        ReportSchema schema,
        Stream output,
        ReportExportOptions options,
        CancellationToken ct,
        Action? onRowFlushed = null) where TRow : class;
}
