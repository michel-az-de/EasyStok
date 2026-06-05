namespace EasyStock.Web.Helpers;

// "Hoje" no fuso BR — usado em telas onde o servidor (Render = UTC) renderizaria
// um dia adiantado durante a janela 21:00–23:59 BRT. Antes Caixa, SaidaFormViewModel
// e similares usavam DateTime.Now/Today direto e mostravam "16/05" quando no BR
// ainda era 15/05.
public static class BrazilTime
{
    // String literal pra evitar reference a EasyStock.Domain (camada inferior).
    // O valor canonico vive em EasyStock.Domain.Defaults.OperacionalDefaults.Timezone.
    private const string IanaId = "America/Sao_Paulo";
    private const string WindowsId = "E. South America Standard Time";

    private static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        // Linux (Render) usa IANA; Windows usa o nome MSFT. Fallback evita crash em dev local.
        try { return TimeZoneInfo.FindSystemTimeZoneById(IanaId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(WindowsId); }
    }

    public static DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);
    public static DateOnly Today() => DateOnly.FromDateTime(Now());

    /// <summary>
    /// Converte um instante UTC (vindo do banco/API) para Brasilia, para exibicao nas telas.
    /// Substitui <c>.ToLocalTime()</c>, que dependia do TZ do servidor (container = UTC) e
    /// renderizava UTC (BUG-10). Robusto p/ Kind Utc/Unspecified (ambos representam UTC).
    /// Espelha EasyStock.Admin.Helpers.BrazilTime.ParaBrasilia (BUG-007).
    /// </summary>
    public static DateTime ParaBrasilia(this DateTime utc)
    {
        // default(DateTime) costuma vir de um TryParse que falhou; converter para um fuso
        // atras de UTC estouraria (underflow), entao devolve como esta.
        if (utc == default) return utc;
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZone);
    }

    public static DateTime? ParaBrasilia(this DateTime? utc)
        => utc.HasValue ? utc.Value.ParaBrasilia() : null;

    /// <summary>Overload p/ DateTimeOffset (alguns models vindos da API): converte o
    /// instante para Brasilia e devolve DateTime (sem offset) pronto p/ formatar.</summary>
    public static DateTime ParaBrasilia(this DateTimeOffset utc)
        => TimeZoneInfo.ConvertTime(utc, TimeZone).DateTime;

    public static DateTime? ParaBrasilia(this DateTimeOffset? utc)
        => utc.HasValue ? utc.Value.ParaBrasilia() : null;
}
