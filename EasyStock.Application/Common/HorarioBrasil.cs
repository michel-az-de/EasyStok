namespace EasyStock.Application.Common;

/// <summary>
/// Dia operacional no fuso de Brasilia (America/Sao_Paulo). O servidor roda em UTC, entao
/// derivar o "dia" de DateTime.UtcNow erra na janela 21h-23h59 BRT (vira o dia seguinte),
/// divergindo do que a tela exibe — causa do BUG-09 do caixa: o card usava BrazilTime.Today()
/// (BRT) e a validacao/abertura usavam DateOnly.FromDateTime(UtcNow) (UTC), dando saldos
/// diferentes na janela noturna. Use isto para agrupar/validar por dia operacional.
///
/// Espelha EasyStock.Web.Helpers.BrazilTime; o valor canonico do TZ vive em
/// EasyStock.Domain.Defaults.OperacionalDefaults.Timezone ("America/Sao_Paulo").
/// </summary>
public static class HorarioBrasil
{
    private const string IanaId = "America/Sao_Paulo";
    private const string WindowsId = "E. South America Standard Time";

    private static readonly TimeZoneInfo Tz = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // Linux (container) usa IANA; Windows (dev local) usa o nome MSFT.
        try { return TimeZoneInfo.FindSystemTimeZoneById(IanaId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(WindowsId); }
    }

    /// <summary>Instante UTC -> data do dia em Brasilia (robusto p/ Kind Utc/Unspecified).</summary>
    public static DateOnly DataOperacional(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asUtc, Tz));
    }

    /// <summary>Dia operacional de "agora" em Brasilia (equivalente ao BrazilTime.Today() da Web).</summary>
    public static DateOnly Hoje() => DataOperacional(DateTime.UtcNow);
}
