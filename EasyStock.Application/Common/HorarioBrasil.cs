namespace EasyStock.Application.Common;

/// <summary>Origem da zona de fuso resolvida em <see cref="HorarioBrasil"/>.</summary>
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
/// Dia operacional no fuso de Brasilia (America/Sao_Paulo). O servidor roda em UTC, entao
/// derivar o "dia" de DateTime.UtcNow erra na janela 21h-23h59 BRT (vira o dia seguinte),
/// divergindo do que a tela exibe — causa do BUG-09 do caixa: o card usava BrazilTime.Today()
/// (BRT) e a validacao/abertura usavam DateOnly.FromDateTime(UtcNow) (UTC), dando saldos
/// diferentes na janela noturna. Use isto para agrupar/validar por dia operacional.
///
/// DUAS CLASSES DE COLUNA, FIXES OPOSTOS (ADR-0032):
///  - Colunas de INSTANTE real (DataMovimento, DataVenda, EntreguEm, PagoEm, CriadoEm):
///    agrupar pelo dia civil de Brasilia com <see cref="JanelaDiaUtc"/> (limites em UTC real,
///    meia-noite de Brasilia = 03:00Z). Comparar `col >= ini && col < fim`. Nunca `.Date`.
///  - Colunas de DATA CIVIL (DataVencimento, gravada como meia-noite-UTC-da-data-civil via
///    DataUtc.ParaUtc): comparar contra <see cref="HojeInstanteUtc"/> (meia-noite UTC = 00:00Z).
///    NAO use JanelaDiaUtc aqui: 03:00Z marcaria como vencido algo que vence hoje.
///
/// Espelha EasyStock.Web.Helpers.BrazilTime; o valor canonico do TZ vive em
/// EasyStock.Domain.Defaults.OperacionalDefaults.Timezone ("America/Sao_Paulo").
/// </summary>
public static class HorarioBrasil
{
    private const string IanaId = "America/Sao_Paulo";
    private const string WindowsId = "E. South America Standard Time";

    private static readonly (TimeZoneInfo Tz, FonteFuso Fonte) Resolvido = Resolver(TimeZoneInfo.FindSystemTimeZoneById);

    private static TimeZoneInfo Tz => Resolvido.Tz;

    /// <summary>Como o fuso foi resolvido neste processo (diagnostico p/ /health e startup).</summary>
    public static FonteFuso Fonte => Resolvido.Fonte;

    /// <summary>True quando nem IANA nem Windows resolveram e caiu na zona fixa -03:00.</summary>
    public static bool Degradado => Resolvido.Fonte == FonteFuso.FallbackFixo;

    /// <summary>Id da zona resolvida (diagnostico p/ /health e startup).</summary>
    public static string ZonaId => Tz.Id;

    /// <summary>Offset atual de Brasilia em minutos (-180 normal; -120 so se o DST voltar).</summary>
    public static int OffsetMinutosAtual() => (int)Tz.GetUtcOffset(DateTime.UtcNow).TotalMinutes;

    /// <summary>
    /// Resolve a zona de Brasilia: IANA -> Windows -> zona fixa -03:00. NUNCA lanca (a versao
    /// antiga lancava se faltasse tzdata no container, derrubando a 1a request). O `finder`
    /// e injetavel para teste do caminho de fallback.
    /// </summary>
    public static (TimeZoneInfo Tz, FonteFuso Fonte) Resolver(Func<string, TimeZoneInfo> finder)
    {
        try { return (finder(IanaId), FonteFuso.Iana); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        try { return (finder(WindowsId), FonteFuso.Windows); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return (ZonaFixaBrasilia(), FonteFuso.FallbackFixo);
    }

    /// <summary>Zona fixa -03:00 sem DST. Brasil nao adota horario de verao desde a Lei
    /// 13.650/2019, entao o offset fixo e correto p/ datas atuais; e a rede de seguranca
    /// para o app subir em qualquer ambiente.</summary>
    public static TimeZoneInfo ZonaFixaBrasilia() => TimeZoneInfo.CreateCustomTimeZone(
        "America/Sao_Paulo (fixo -03:00)", TimeSpan.FromHours(-3), "Horario de Brasilia (fixo)", "BRT");

    /// <summary>Instante UTC -> data do dia em Brasilia (robusto p/ Kind Utc/Unspecified).</summary>
    public static DateOnly DataOperacional(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asUtc, Tz));
    }

    /// <summary>Dia operacional de "agora" em Brasilia (equivalente ao BrazilTime.Today() da Web).</summary>
    public static DateOnly Hoje() => DataOperacional(DateTime.UtcNow);

    /// <summary>
    /// Janela UTC [ini, fim) correspondente ao dia civil de Brasilia, para filtrar colunas de
    /// INSTANTE real (timestamptz gravado de UtcNow). Ex.: `WHERE col >= ini && col < fim`.
    /// A meia-noite de Brasilia vira 03:00Z (offset -03:00). DST-safe.
    /// </summary>
    public static (DateTime IniUtc, DateTime FimUtc) JanelaDiaUtc(DateOnly? dia = null)
    {
        var d = dia ?? Hoje();
        return (InicioRealDoDiaUtc(d), InicioRealDoDiaUtc(d.AddDays(1)));
    }

    /// <summary>
    /// Instante UTC real correspondente a meia-noite (00:00) de Brasilia do dia informado.
    /// DST-safe: se a meia-noite cair no gap de mola (inexistente, como era em SP a meia-noite),
    /// avanca para o 1o instante valido; se ambigua (outono), ConvertTimeToUtc assume o horario
    /// padrao. Com a zona fixa -03:00 nenhum dos casos ocorre.
    /// </summary>
    public static DateTime InicioRealDoDiaUtc(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue); // Kind=Unspecified, meia-noite "local" de Brasilia
        if (Tz.IsInvalidTime(local))
            local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, Tz);
    }

    /// <summary>
    /// "Hoje" de Brasilia expresso como meia-noite UTC (00:00Z), para comparar com colunas de
    /// DATA CIVIL (gravadas como meia-noite-UTC-da-data-civil via DataUtc.ParaUtc). Ex.:
    /// `WHERE dataVencimento &lt; HojeInstanteUtc()`. NAO confundir com <see cref="JanelaDiaUtc"/>
    /// (03:00Z), que e para colunas de instante real.
    /// </summary>
    public static DateTime HojeInstanteUtc() => CivilComoInstanteUtc(Hoje());

    /// <summary>Uma data civil de Brasilia expressa como meia-noite UTC (00:00Z), forma pura
    /// e testavel de <see cref="HojeInstanteUtc"/>.</summary>
    public static DateTime CivilComoInstanteUtc(DateOnly dia)
        => DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}
