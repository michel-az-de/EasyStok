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
}
