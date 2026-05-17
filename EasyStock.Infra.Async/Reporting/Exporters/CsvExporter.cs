using System.Globalization;
using System.Reflection;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Infra.Async.Reporting.Exporters;

/// <summary>
/// Exporter CSV usando CsvHelper.
/// Suporte completo a culture pt-BR, separador ";", encoding UTF-8 BOM, datas brasileiras.
/// Streaming via IAsyncEnumerable — sem materializar a coleção em memória.
/// </summary>
public sealed class CsvExporter : IReportExporter
{
    public ReportFormat Format      => ReportFormat.Csv;
    public string ContentType       => "text/csv; charset=utf-8";
    public string FileExtension     => ".csv";

    public async Task WriteAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        ReportSchema schema,
        Stream output,
        ReportExportOptions options,
        CancellationToken ct,
        Action? onRowFlushed = null)
        where TRow : class
    {
        if (options.WriteUtf8Bom)
            await output.WriteAsync(Encoding.UTF8.GetPreamble(), ct);

        var encoding = options.EffectiveEncoding;
        var culture  = options.EffectiveCulture;

        var cfg = new CsvConfiguration(culture)
        {
            Delimiter    = options.CsvDelimiter,
            NewLine      = "\r\n",
            Encoding     = encoding,
            HasHeaderRecord = true,
            // Quoting RFC 4180: cotar somente quando necessário
            ShouldQuote  = args =>
                args.Field?.Contains(options.CsvDelimiter) == true ||
                args.Field?.Contains('"')  == true ||
                args.Field?.Contains('\n') == true ||
                args.Field?.Contains('\r') == true,
        };

        await using var writer = new StreamWriter(output, encoding, leaveOpen: true);
        await using var csv    = new CsvWriter(writer, cfg, leaveOpen: true);

        // Registrar mapeamento com ordem e formatação de colunas do schema
        RegisterSchemaMap<TRow>(csv.Context, schema, culture);

        // Escrever cabeçalho
        csv.WriteHeader<TRow>();
        await csv.NextRecordAsync();

        int flushCounter = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            csv.WriteRecord(row);
            await csv.NextRecordAsync();
            onRowFlushed?.Invoke();
            if (++flushCounter % options.FlushEveryRows == 0)
                await writer.FlushAsync(ct);
        }

        await writer.FlushAsync(ct);
    }

    // ── Mapeamento dinâmico via schema ────────────────────────────────────────

    private static void RegisterSchemaMap<TRow>(
        CsvContext context, ReportSchema schema, CultureInfo defaultCulture)
        where TRow : class
    {
        var map   = new DefaultClassMap<TRow>();
        var props = typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < schema.Columns.Count; i++)
        {
            var col  = schema.Columns[i];
            var prop = props.FirstOrDefault(p =>
                string.Equals(p.Name, col.PropertyName, StringComparison.OrdinalIgnoreCase));

            if (prop is null) continue;

            var memberMap = map.Map(typeof(TRow), prop)
                               .Name(col.HeaderLabel)
                               .Index(i);

            // Aplicar formato de cultura/data/número conforme schema
            var colCulture = col.CultureOverride ?? defaultCulture;
            memberMap.TypeConverterOption.CultureInfo(colCulture);

            if (col.FormatString is not null)
                memberMap.TypeConverterOption.Format(col.FormatString);
        }

        context.RegisterClassMap(map);
    }
}
