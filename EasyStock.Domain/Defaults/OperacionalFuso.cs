namespace EasyStock.Domain.Defaults;

/// <summary>
/// Utilitario minimo de fuso para o Domain: converte instante UTC para a data
/// civil de Brasilia (DateOnly). Usado pelas entidades e servicos de dominio que
/// recebem timestamps de operacao e precisam do dia operacional para comparacoes
/// de validade — sem depender da camada Application (HorarioBrasil vive la).
///
/// Logica IANA→Windows→fixo identica a HorarioBrasil. O valor canonico do TZ
/// vive em OperacionalDefaults.Timezone ("America/Sao_Paulo").
/// </summary>
internal static class OperacionalFuso
{
    private static readonly TimeZoneInfo Tz = Resolver();

    private static TimeZoneInfo Resolver()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(OperacionalDefaults.Timezone); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        // Brasil sem DST desde Lei 13.650/2019; fixo e correto para datas atuais.
        return TimeZoneInfo.CreateCustomTimeZone(
            "America/Sao_Paulo (fixo -03:00)", TimeSpan.FromHours(-3), "Horario de Brasilia (fixo)", "BRT");
    }

    /// <summary>
    /// Instante UTC → data civil em Brasília. Robusto para Kind Utc/Unspecified.
    /// Usado por entidades de domínio que recebem timestamps de operacao e precisam
    /// verificar validade contra o dia operacional correto.
    /// </summary>
    public static DateOnly DataOperacional(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asUtc, Tz));
    }
}
