using System.IO.Compression;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;
using EasyStock.Domain.Reporting;

namespace EasyStock.Infra.Async.Reporting.Exporters;

/// <summary>
/// Exporter especial para o relatório "XMLs autorizados (em lote)".
/// Consome <see cref="XmlBulkDownloadRow"/> e compõe um arquivo ZIP em streaming,
/// baixando cada XML do storage individualmente via <see cref="IFileStorage.DownloadStreamAsync"/>
/// sem jamais carregar todos os XMLs em memória simultaneamente.
/// </summary>
public sealed class XmlBulkZipExporter(IFileStorage fileStorage) : IReportExporter
{
    public ReportFormat Format        => ReportFormat.Zip;
    public string       ContentType   => "application/zip";
    public string       FileExtension => ".zip";

    public async Task WriteAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        ReportSchema           schema,
        Stream                 output,
        ReportExportOptions    options,
        CancellationToken      ct,
        Action?                onRowFlushed = null) where TRow : class
    {
        if (rows is not IAsyncEnumerable<XmlBulkDownloadRow> xmlRows)
            throw new InvalidOperationException(
                $"{nameof(XmlBulkZipExporter)} requer IAsyncEnumerable<{nameof(XmlBulkDownloadRow)}>. " +
                $"Tipo recebido: {typeof(TRow).Name}.");

        // ZipArchive em modo Create sobre o stream de saída.
        // leaveOpen: true para que o pipeline de upload controle quando fechar o stream.
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await foreach (var row in xmlRows.WithCancellation(ct))
        {
            // Cria a entrada no ZIP com o nome do arquivo (sem subdiretório).
            var entry = zip.CreateEntry(row.NomeArquivo, CompressionLevel.Optimal);

            await using var entryStream  = entry.Open();
            await using var xmlStream    = await fileStorage.DownloadStreamAsync(row.StorageKey, ct);

            await xmlStream.CopyToAsync(entryStream, ct);

            onRowFlushed?.Invoke();
        }
        // zip.Dispose() é chamado pelo using — finaliza e fecha o arquivo ZIP.
    }
}
