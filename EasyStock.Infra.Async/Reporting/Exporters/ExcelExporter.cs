using System.Data;
using System.Globalization;
using System.Reflection;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace EasyStock.Infra.Async.Reporting.Exporters;

/// <summary>
/// Exporter XLSX usando MiniExcel (streaming SAX-like, baixo consumo de memória).
/// Adapta IAsyncEnumerable&lt;TRow&gt; para IDataReader via AsyncEnumerableDataReader&lt;T&gt;
/// (B-06 do plano: sync-over-async seguro no Worker — sem SynchronizationContext).
/// </summary>
public sealed class ExcelExporter : IReportExporter
{
    public ReportFormat Format    => ReportFormat.Xlsx;
    public string ContentType     => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension   => ".xlsx";

    public async Task WriteAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        ReportSchema schema,
        Stream output,
        ReportExportOptions options,
        CancellationToken ct,
        Action? onRowFlushed = null)
        where TRow : class
    {
        // Adapta IAsyncEnumerable→IDataReader; bloqueia em MoveNextAsync (sync-over-async).
        // IDataReader.GetName(i) retorna os labels corretos do ReportSchema, então
        // MiniExcel usa esses nomes como cabeçalhos sem precisar de DynamicColumns.
        using var reader = new AsyncEnumerableDataReader<TRow>(
            rows.GetAsyncEnumerator(ct), schema, options, onRowFlushed);

        var config = new OpenXmlConfiguration
        {
            TableStyles = TableStyles.None,
            AutoFilter  = false,
            FastMode    = true,
        };

        // #364: o MiniExcel exige um IDataReader SÍNCRONO (não há overload IAsyncEnumerable),
        // então o Read() pontua async→sync (MoveNextAsync().GetResult()) e BLOQUEIA uma thread
        // pela duração inteira da serialização. Rodamos a versão síncrona MiniExcel.SaveAs numa
        // thread DEDICADA (LongRunning), fora do ThreadPool — assim N exports concorrentes no
        // Worker não famintam o pool compartilhado (outros relatórios + hosted services). O
        // cancelamento continua honrado: o enumerator foi criado com `ct` (GetAsyncEnumerator
        // acima), então MoveNextAsync observa o token. SaveAs síncrono não recebe ct — por isso
        // não usamos SaveAsAsync (cujas continuations voltariam ao pool, anulando o ganho).
        await Task.Factory.StartNew(
            () => MiniExcel.SaveAs(output, reader, sheetName: schema.Title, configuration: config),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

}

// ── AsyncEnumerableDataReader<T> ─────────────────────────────────────────────

/// <summary>
/// Adapta IAsyncEnumerable&lt;T&gt; para IDataReader (interface síncrona do MiniExcel).
/// Bloqueia a thread em Read() via GetAwaiter().GetResult() — seguro no Worker
/// (Generic Host sem SynchronizationContext, sem risco de deadlock).
/// Formata valores usando o ReportSchema (datas, decimais pt-BR).
/// </summary>
internal sealed class AsyncEnumerableDataReader<T> : IDataReader
{
    private readonly IAsyncEnumerator<T> _enumerator;
    private readonly ReportColumn[]      _columns;
    private readonly PropertyInfo[]      _props;
    private readonly Func<T, int, object>[] _accessors;
    private T?   _current;
    private bool _disposed;

    public AsyncEnumerableDataReader(
        IAsyncEnumerator<T>  enumerator,
        ReportSchema         schema,
        ReportExportOptions  options,
        Action?              onRowFlushed)
    {
        _enumerator = enumerator;
        _columns    = schema.Columns.OrderBy(c => c.Order).ToArray();

        var typeProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        _props = _columns
            .Select(c => typeProps.First(p =>
                string.Equals(p.Name, c.PropertyName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        // Pré-compila acessores com formatação pt-BR
        _accessors = BuildAccessors(_columns, _props, options.EffectiveCulture, options, onRowFlushed);
    }

    // IDataReader / IDataRecord

    public bool   Read()
    {
        bool hasNext = _enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult();
        _current = hasNext ? _enumerator.Current : default;
        return hasNext;
    }

    public int    FieldCount   => _columns.Length;
    public string GetName(int i) => _columns[i].HeaderLabel;
    public object GetValue(int i) => _current is null ? DBNull.Value : _accessors[i](_current, i);

    public int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public bool   IsDBNull(int i)     => _current is null || GetValue(i) is DBNull;
    public bool   GetBoolean(int i)   => (bool)GetValue(i);
    public byte   GetByte(int i)      => (byte)GetValue(i);
    public long   GetBytes(int i, long fo, byte[]? b, int bo, int l) => 0;
    public char   GetChar(int i)      => (char)GetValue(i);
    public long   GetChars(int i, long fo, char[]? b, int bo, int l) => 0;
    public Guid   GetGuid(int i)      => (Guid)GetValue(i);
    public short  GetInt16(int i)     => (short)GetValue(i);
    public int    GetInt32(int i)     => (int)GetValue(i);
    public long   GetInt64(int i)     => (long)GetValue(i);
    public float  GetFloat(int i)     => (float)GetValue(i);
    public double GetDouble(int i)    => (double)GetValue(i);
    public string GetString(int i)    => GetValue(i)?.ToString() ?? "";
    public decimal GetDecimal(int i)  => (decimal)GetValue(i);
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public IDataReader GetData(int i)  => throw new NotSupportedException();
    public string GetDataTypeName(int i) => _props[i].PropertyType.Name;
    public Type   GetFieldType(int i)   => _props[i].PropertyType;
    public int    GetOrdinal(string n)  =>
        Array.FindIndex(_columns, c => string.Equals(c.HeaderLabel, n, StringComparison.OrdinalIgnoreCase));

    // IDataReader extras (não necessários para MiniExcel mas parte da interface)
    public object this[int i]    => GetValue(i);
    public object this[string n] => GetValue(GetOrdinal(n));
    public int  Depth      => 0;
    public bool IsClosed   => _disposed;
    public int  RecordsAffected => -1;
    public bool NextResult() => false;
    public void Close()     => Dispose();

    public DataTable? GetSchemaTable()
    {
        var dt = new DataTable("SchemaTable");
        dt.Columns.Add("ColumnName", typeof(string));
        dt.Columns.Add("DataType", typeof(Type));
        for (int i = 0; i < _columns.Length; i++)
            dt.Rows.Add(_columns[i].HeaderLabel, _props[i].PropertyType);
        return dt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Acessores pré-compilados ──────────────────────────────────────────────

    private static Func<T, int, object>[] BuildAccessors(
        ReportColumn[]      columns,
        PropertyInfo[]      props,
        CultureInfo         defaultCulture,
        ReportExportOptions options,
        Action?             onRowFlushed)
    {
        var accessors = new Func<T, int, object>[columns.Length];
        bool firstColumn = true; // onRowFlushed dispara na primeira coluna lida de cada row

        for (int i = 0; i < columns.Length; i++)
        {
            var col        = columns[i];
            var prop       = props[i];
            var culture    = col.CultureOverride ?? defaultCulture;
            var formatStr  = col.FormatString;
            var colIndex   = i;
            var isFirst    = firstColumn;
            firstColumn    = false;

            accessors[i] = (row, idx) =>
            {
                var raw = prop.GetValue(row);
                if (raw is null) return DBNull.Value;

                // Disparar callback na primeira coluna de cada linha
                if (isFirst) onRowFlushed?.Invoke();

                // Formatar valor para exibição no Excel (strings br-formatadas)
                return FormatValue(raw, formatStr, culture, options);
            };
        }
        return accessors;
    }

    private static object FormatValue(
        object raw, string? formatStr, CultureInfo culture, ReportExportOptions options)
    {
        return raw switch
        {
            DateTime dt    => formatStr is not null
                                  ? dt.ToString(formatStr, culture)
                                  : dt.ToString(options.DefaultDateTimeFormat, culture),
            DateOnly d     => formatStr is not null
                                  ? d.ToString(formatStr, culture)
                                  : d.ToString(options.DefaultDateFormat, culture),
            DateTimeOffset dto => formatStr is not null
                                  ? dto.ToString(formatStr, culture)
                                  : dto.ToString(options.DefaultDateTimeFormat, culture),
            decimal dec    => dec, // Excel armazena decimal nativo
            float f        => f,
            double d       => d,
            bool b         => b ? "Sim" : "Não",
            _              => raw
        };
    }
}
