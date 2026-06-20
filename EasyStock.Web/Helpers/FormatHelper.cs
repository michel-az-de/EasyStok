using System.Globalization;
using System.Net;
using System.Net.Sockets;

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

    /// <summary>
    /// Decimal para o atributo <c>value</c>/<c>max</c> de <c>&lt;input type="number"&gt;</c>: SEMPRE
    /// InvariantCulture ("1234.56"). Sob pt-BR, <c>@valor</c> cru vira "1.234,56", que o input HTML
    /// rejeita como número inválido (campo renderiza vazio). O binder de entrada já parseia invariante.
    /// </summary>
    public static string AsInputDecimal(this decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string AsInputDecimal(this decimal? value) =>
        value.HasValue ? value.Value.AsInputDecimal() : "";

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

    /// <summary>
    /// SAID-01: nao expor IP de infraestrutura interna (bridge docker / rede privada) na UI
    /// de auditoria. Retorna o IP SOMENTE quando for publico/roteavel; senao null (a view
    /// oculta). Normaliza IPv4-mapped IPv6 (::ffff:172.18.0.5 -> 172.18.0.5) e lista XFF
    /// ("a, b" -> primeiro token). Enquanto a captura nao propaga o IP real do cliente
    /// (Web->API server-to-server, ver issue), a maioria cai como interno e fica oculta.
    /// </summary>
    public static string? AsIpPublico(this string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var first = ip.Split(',')[0].Trim();
        if (!IPAddress.TryParse(first, out var addr)) return null;
        if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
        return EhIpInterno(addr) ? null : addr.ToString();
    }

    /// <summary>True para loopback e ranges privados/link-local/ULA (RFC 1918 / 4193 / 4291).</summary>
    private static bool EhIpInterno(IPAddress addr)
    {
        if (IPAddress.IsLoopback(addr)) return true;
        var b = addr.GetAddressBytes();
        if (addr.AddressFamily == AddressFamily.InterNetwork) // IPv4
        {
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254); // link-local
        }
        if (b.Length == 16) // IPv6
        {
            if ((b[0] & 0xFE) == 0xFC) return true;                 // fc00::/7 (ULA)
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true; // fe80::/10 (link-local)
        }
        return false;
    }

    /// <summary>
    /// Texto amigável do badge de validade a partir dos dias até o vencimento
    /// (negativo = já vencido). Corrige o "-2361 d" cru do QA (EST-01):
    ///   &lt; 0 -> "vencido há N dias" · 0 -> "vence hoje" · &gt; 0 -> "N dias".
    /// </summary>
    public static string AsValidadeBadge(this int dias) => dias switch
    {
        < 0 => $"vencido há {-dias} {(dias == -1 ? "dia" : "dias")}",
        0 => "vence hoje",
        _ => $"{dias} {(dias == 1 ? "dia" : "dias")}",
    };

    public static string AsValidadeBadge(this int? dias) =>
        dias.HasValue ? dias.Value.AsValidadeBadge() : "";
}
