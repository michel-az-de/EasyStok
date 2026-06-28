namespace EasyStock.Admin.Helpers;

/// <summary>
/// Conversao de instantes UTC (vindos da API) para o horario de Brasilia, para exibicao
/// nas telas. Substitui <c>.ToLocalTime()</c>, que dependia do TZ do servidor (container =
/// UTC) e renderizava UTC, gerando inconsistencia entre telas (BUG-007). Espelha
/// EasyStock.Web.Helpers.BrazilTime (#479).
///
/// String IANA literal porque o Admin e um proxy HTTP sem ProjectReference para o Domain
/// (onde vive o valor canonico OperacionalDefaults.Timezone).
/// </summary>
public static class BrazilTime
{
    private const string IanaId = "America/Sao_Paulo";
    private const string WindowsId = "E. South America Standard Time";

    private static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        // Linux (container) usa IANA; Windows (dev local) usa o nome MSFT; se nada resolver
        // (ex.: imagem sem tzdata) cai na zona fixa -03:00 em vez de derrubar a tela. Brasil
        // sem DST desde a Lei 13.650/2019, entao o offset fixo e correto p/ datas atuais.
        try { return TimeZoneInfo.FindSystemTimeZoneById(IanaId); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById(WindowsId); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone(
            "America/Sao_Paulo (fixo -03:00)", TimeSpan.FromHours(-3), "Horario de Brasilia (fixo)", "BRT");
    }

    /// <summary>
    /// Converte um instante UTC para Brasilia. Datas da API chegam com Kind=Utc (parse de
    /// ISO com 'Z') ou Unspecified (sem offset); ambos representam UTC, entao normalizamos
    /// para Utc antes de converter, sem depender do TZ do servidor como o ToLocalTime fazia.
    /// </summary>
    public static DateTime ParaBrasilia(this DateTime utc)
    {
        // default(DateTime) = MinValue costuma vir de um TryParse que falhou. Converter para
        // um fuso atras de UTC estouraria (underflow), entao devolve como esta.
        if (utc == default) return utc;
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZone);
    }

    /// <summary>Overload para <see cref="Nullable{DateTime}"/>: null entra, null sai.</summary>
    public static DateTime? ParaBrasilia(this DateTime? utc)
        => utc.HasValue ? utc.Value.ParaBrasilia() : null;

    /// <summary>
    /// Tempo relativo em pt-BR para exibicao ("ha 3 min", "ha 2 h", "ha 5 dias"). Acima de
    /// 7 dias cai para data absoluta dd/MM/yyyy no fuso de Brasilia. Input UTC (Kind Utc ou
    /// Unspecified, ambos tratados como UTC, espelhando ParaBrasilia). Consolida as copias
    /// inline que viviam em Detail.cshtml/format.js (issue 730). Faixa de cor (ambar/vermelho
    /// por idade) e decidida no call-site, nao aqui.
    /// </summary>
    public static string FormatRelative(this DateTime utc)
    {
        if (utc == default) return string.Empty;
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var delta = DateTime.UtcNow - asUtc;
        if (delta < TimeSpan.Zero || delta.TotalSeconds < 60) return "agora há pouco";
        if (delta.TotalMinutes < 60) return $"há {(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"há {(int)delta.TotalHours} h";
        if (delta.TotalDays < 7) { var d = (int)delta.TotalDays; return $"há {d} {(d == 1 ? "dia" : "dias")}"; }
        return asUtc.ParaBrasilia().ToString("dd/MM/yyyy");
    }

    /// <summary>Overload nullable: null devolve string vazia.</summary>
    public static string FormatRelative(this DateTime? utc)
        => utc.HasValue ? utc.Value.FormatRelative() : string.Empty;
}
