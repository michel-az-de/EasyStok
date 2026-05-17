using System.Globalization;

namespace EasyStock.Web.Helpers;

/// <summary>
/// Helpers de formatacao pt-BR para uso em Razor views.
/// Use com extension method syntax: <c>@Model.Total.AsMoney()</c>.
/// </summary>
public static class FormatHelper
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string AsMoney(this decimal value) =>
        value.ToString("C", PtBr);

    public static string AsMoney(this decimal? value) =>
        value.HasValue ? value.Value.AsMoney() : "";

    public static string AsMoney(this double value) =>
        ((decimal)value).AsMoney();

    public static string AsDate(this DateTime value) =>
        value.ToString("dd/MM/yyyy", PtBr);

    public static string AsDate(this DateTime? value) =>
        value.HasValue ? value.Value.AsDate() : "";

    public static string AsDate(this DateTimeOffset value) =>
        value.LocalDateTime.AsDate();

    public static string AsDate(this DateTimeOffset? value) =>
        value.HasValue ? value.Value.AsDate() : "";

    public static string AsDate(this DateOnly value) =>
        value.ToString("dd/MM/yyyy", PtBr);

    public static string AsDate(this DateOnly? value) =>
        value.HasValue ? value.Value.AsDate() : "";

    public static string AsDateTime(this DateTime value) =>
        value.ToString("dd/MM/yyyy 'às' HH:mm", PtBr);

    public static string AsDateTime(this DateTime? value) =>
        value.HasValue ? value.Value.AsDateTime() : "";

    public static string AsDateTime(this DateTimeOffset value) =>
        value.LocalDateTime.AsDateTime();

    public static string AsDateTime(this DateTimeOffset? value) =>
        value.HasValue ? value.Value.AsDateTime() : "";

    /// <summary>
    /// 4 faixas explicitas:
    ///   delta &lt; 1h  -> "há X min"
    ///   delta &lt; 24h -> "há X horas"
    ///   delta &lt; 7d  -> "há X dias"
    ///   delta >= 7d   -> "dd/mm/aaaa" (data absoluta)
    /// Tooltip recomendado em title=: <c>@d.AsDateTime()</c>.
    /// </summary>
    public static string AsRelativeDate(this DateTime value, DateTime? now = null)
    {
        var reference = now ?? DateTime.UtcNow;
        var v = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var delta = reference - v;

        if (delta.TotalSeconds < 0)
        {
            // futuro: cai no absoluto pra evitar "há -2 min"
            return value.AsDateTime();
        }

        if (delta.TotalMinutes < 60)
        {
            var min = Math.Max(1, (int)delta.TotalMinutes);
            return $"há {min} min";
        }
        if (delta.TotalHours < 24)
        {
            var h = (int)delta.TotalHours;
            return h == 1 ? "há 1 hora" : $"há {h} horas";
        }
        if (delta.TotalDays < 7)
        {
            var d = (int)delta.TotalDays;
            return d == 1 ? "há 1 dia" : $"há {d} dias";
        }
        return value.AsDate();
    }

    public static string AsRelativeDate(this DateTime? value, DateTime? now = null) =>
        value.HasValue ? value.Value.AsRelativeDate(now) : "";

    public static string AsRelativeDate(this DateTimeOffset value, DateTime? now = null) =>
        value.UtcDateTime.AsRelativeDate(now);

    public static string AsRelativeDate(this DateTimeOffset? value, DateTime? now = null) =>
        value.HasValue ? value.Value.AsRelativeDate(now) : "";

    /// <summary>
    /// "120 un", "2,5 kg", "0,75 L". Inteiros sem casas decimais.
    /// </summary>
    public static string AsQuantity(this decimal value, string unit = "un")
    {
        string formatted;
        if (value % 1 == 0)
        {
            formatted = ((long)value).ToString("N0", PtBr);
        }
        else
        {
            formatted = value.ToString("0.###", PtBr);
        }
        return string.IsNullOrWhiteSpace(unit) ? formatted : $"{formatted} {unit}";
    }

    public static string AsQuantity(this decimal? value, string unit = "un") =>
        value.HasValue ? value.Value.AsQuantity(unit) : "";

    public static string AsQuantity(this int value, string unit = "un") =>
        ((decimal)value).AsQuantity(unit);
}
