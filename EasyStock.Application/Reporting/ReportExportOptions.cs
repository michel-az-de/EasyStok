using System.Globalization;
using System.Text;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Opções de formatação aplicadas ao exporter.
/// </summary>
public sealed record ReportExportOptions(
    CultureInfo? Culture = null,
    string CsvDelimiter = ";",
    bool WriteUtf8Bom = true,
    string DefaultDateFormat = "dd/MM/yyyy",
    string DefaultDateTimeFormat = "dd/MM/yyyy HH:mm:ss",
    string DefaultDecimalFormat = "0.00",
    int FlushEveryRows = 1000,
    Encoding? TextEncoding = null)
{
    /// <summary>Culture padrão pt-BR usada quando <see cref="Culture"/> não é especificada.</summary>
    public static readonly CultureInfo DefaultCulture =
        CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Culture efetiva: a fornecida ou pt-BR.</summary>
    public CultureInfo EffectiveCulture => Culture ?? DefaultCulture;

    /// <summary>Encoding efetivo: o fornecido ou UTF-8.</summary>
    public Encoding EffectiveEncoding => TextEncoding ?? Encoding.UTF8;
}
