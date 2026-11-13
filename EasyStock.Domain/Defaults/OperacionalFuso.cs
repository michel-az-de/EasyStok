namespace EasyStock.Domain.Defaults;

/// <summary>Origem da zona de fuso resolvida em <see cref="OperacionalFuso"/>.</summary>
public enum FonteFuso
{
    /// <summary>Resolvida pelo id IANA "America/Sao_Paulo" (Linux/container, ideal).</summary>
    Iana,
    /// <summary>Resolvida pelo id Windows "E. South America Standard Time" (dev Windows).</summary>
    Windows,
    /// <summary>Nem IANA nem Windows disponiveis (ex.: container sem tzdata): caiu na
    /// zona fixa -03:00. App sobe, mas opera em modo degradado — startup em producao
    /// deve recusar subir nesse caso (ver StartupHardening.ValidateTimezone).</summary>
    FallbackFixo,
}

/// <summary>
/// #2018: o fuso de Brasilia (America/Sao_Paulo) resolvido num lugar so. Domain, Application (HorarioBrasil, que delega
/// para ca), a agenda da loja (ExpedienteLoja) e os e-mails de seguranca da conta usam a mesma zona, com a mesma
/// ordem de fallback: IANA, Windows, fixo -03:00. Nenhum outro ponto chama <c>FindSystemTimeZoneById</c> para Brasilia.
/// O valor canonico do TZ vive em <see cref="OperacionalDefaults.Timezone"/>.
/// </summary>
public static class OperacionalFuso
{
    private const string WindowsId = "E. South America Standard Time";

    private static readonly (TimeZoneInfo Tz, FonteFuso Fonte) Resolvido = Resolver(TimeZoneInfo.FindSystemTimeZoneById);

    /// <summary>A zona de Brasilia deste processo.</summary>
    public static TimeZoneInfo Zona => Resolvido.Tz;

    /// <summary>Como o fuso foi resolvido neste processo (diagnostico p/ /health e startup).</summary>
    public static FonteFuso Fonte => Resolvido.Fonte;

    /// <summary>
    /// Resolve a zona de Brasilia: IANA -> Windows -> zona fixa -03:00. NUNCA lanca (a versao antiga lancava se
    /// faltasse tzdata no container, derrubando a 1a request). O <paramref name="finder"/> e injetavel para testar o fallback.
    /// </summary>
    public static (TimeZoneInfo Tz, FonteFuso Fonte) Resolver(Func<string, TimeZoneInfo> finder)
    {
        try { return (finder(OperacionalDefaults.Timezone), FonteFuso.Iana); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        try { return (finder(WindowsId), FonteFuso.Windows); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return (ZonaFixaBrasilia(), FonteFuso.FallbackFixo);
    }

    /// <summary>Zona fixa -03:00 sem DST. Brasil nao adota horario de verao desde a Lei 13.650/2019, entao o offset
    /// fixo e correto p/ datas atuais; e a rede de seguranca para o app subir em qualquer ambiente.</summary>
    public static TimeZoneInfo ZonaFixaBrasilia() => TimeZoneInfo.CreateCustomTimeZone(
        "America/Sao_Paulo (fixo -03:00)", TimeSpan.FromHours(-3), "Horario de Brasilia (fixo)", "BRT");

    /// <summary>
    /// Instante UTC → data civil em Brasília. Robusto para Kind Utc/Unspecified.
    /// Usado por entidades de domínio que recebem timestamps de operacao e precisam
    /// verificar validade contra o dia operacional correto.
    /// </summary>
    public static DateOnly DataOperacional(DateTime utc) => DateOnly.FromDateTime(ParaBrasilia(utc));

    /// <summary>Instante UTC → data e hora de parede em Brasília (Kind=Unspecified). Só para exibir; não persistir.</summary>
    public static DateTime ParaBrasilia(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Zona);
    }
}
