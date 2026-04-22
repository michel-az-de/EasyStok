using EasyStock.Api.Configuration;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Helpers internos compartilhados entre os controllers de Diagnóstico
/// (split em <see cref="DiagnosticoController"/>, <see cref="DiagnosticoLogsController"/>
/// e <see cref="DiagnosticoInfraController"/>).
/// </summary>
internal static class DiagnosticoHelpers
{
    public static string GetLogDirectory(IConfiguration configuration) =>
        configuration[ConfigurationKeys.LogDirectory] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");
}
