using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace EasyStock.Infra.Async.Logging;

/// <summary>
/// <c>WriteTo.Redigido(...)</c>: todo sink configurado dentro dele recebe o evento já redigido (I4.5, #1698).
/// No <c>appsettings.json</c>, com <c>"EasyStock.Infra.Async"</c> em <c>Serilog:Using</c>:
/// <code>{ "Name": "Redigido", "Args": { "configure": [ { "Name": "Console", "Args": { ... } } ] } }</code>
/// Mesmo formato do <c>WriteTo.Async</c>.
/// </summary>
public static class RedacaoSerilogExtensions
{
    public static LoggerConfiguration Redigido(
        this LoggerSinkConfiguration sinks,
        Action<LoggerSinkConfiguration> configure,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(configure);
        return LoggerSinkConfiguration.Wrap(sinks, interno => new SinkQueRedige(interno), configure, restrictedToMinimumLevel, levelSwitch);
    }
}