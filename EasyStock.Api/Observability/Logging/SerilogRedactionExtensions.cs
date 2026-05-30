using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace EasyStock.Api.Observability.Logging;

/// <summary>
/// Extension methods Serilog para registrar sinks com redaction write-time.
/// Descoberto via reflection: appsettings.json precisa ter "EasyStock.Api" no
/// array "Using" para Serilog encontrar estes metodos.
/// </summary>
public static class SerilogRedactionExtensions
{
    /// <summary>
    /// Mesma assinatura do <c>WriteTo.File</c> nativo, mas envolve o output em
    /// <see cref="RedactingTextFormatter"/>. Drop-in replacement em appsettings.json:
    /// trocar <c>"Name": "File"</c> por <c>"Name": "RedactedFile"</c>.
    ///
    /// Defaults espelham o sink File padrao para minimizar atrito de migracao.
    /// </summary>
    public static LoggerConfiguration RedactedFile(
        this LoggerSinkConfiguration sinkConfig,
        string path,
        string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        long? fileSizeLimitBytes = 1024 * 1024 * 1024,
        int? retainedFileCountLimit = 31,
        bool rollOnFileSizeLimit = false,
        bool shared = false,
        TimeSpan? flushToDiskInterval = null)
    {
        ArgumentNullException.ThrowIfNull(sinkConfig);
        ArgumentNullException.ThrowIfNull(path);
        var formatter = new RedactingTextFormatter(outputTemplate);
        return sinkConfig.File(
            formatter,
            path,
            restrictedToMinimumLevel: restrictedToMinimumLevel,
            fileSizeLimitBytes: fileSizeLimitBytes,
            buffered: false,
            shared: shared,
            flushToDiskInterval: flushToDiskInterval,
            rollingInterval: rollingInterval,
            rollOnFileSizeLimit: rollOnFileSizeLimit,
            retainedFileCountLimit: retainedFileCountLimit);
    }
}
