using EasyStock.Domain.Defaults;

namespace EasyStock.Application.Common;

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
/// A zona e resolvida uma vez so, em <see cref="OperacionalFuso"/> (Domain), e esta classe delega para ela.
/// </summary>
public static class HorarioBrasil
{
    private static TimeZoneInfo Tz => OperacionalFuso.Zona;

    /// <summary>Como o fuso foi resolvido neste processo (diagnostico p/ /health e startup).</summary>
    public static FonteFuso Fonte => OperacionalFuso.Fonte;

    /// <summary>True quando nem IANA nem Windows resolveram e caiu na zona fixa -03:00.</summary>
    public static bool Degradado => Fonte == FonteFuso.FallbackFixo;

    /// <summary>Id da zona resolvida (diagnostico p/ /health e startup).</summary>
    public static string ZonaId => Tz.Id;

    /// <summary>Offset atual de Brasilia em minutos (-180 normal; -120 so se o DST voltar).</summary>
    public static int OffsetMinutosAtual() => (int)Tz.GetUtcOffset(DateTime.UtcNow).TotalMinutes;

    /// <summary>Resolve a zona de Brasilia (IANA, Windows, fixo -03:00); a regra vive em <see cref="OperacionalFuso"/>.</summary>
    public static (TimeZoneInfo Tz, FonteFuso Fonte) Resolver(Func<string, TimeZoneInfo> finder) => OperacionalFuso.Resolver(finder);

    /// <summary>Zona fixa -03:00 sem DST (a rede de seguranca de <see cref="OperacionalFuso"/>).</summary>
    public static TimeZoneInfo ZonaFixaBrasilia() => OperacionalFuso.ZonaFixaBrasilia();

    /// <summary>Instante UTC -> data do dia em Brasilia (robusto p/ Kind Utc/Unspecified).</summary>
    public static DateOnly DataOperacional(DateTime utc) => OperacionalFuso.DataOperacional(utc);

    /// <summary>Dia operacional de "agora" em Brasilia (equivalente ao BrazilTime.Today() da Web).</summary>
    public static DateOnly Hoje() => DataOperacional(DateTime.UtcNow);

    /// <summary>
    /// Instante UTC -> data E hora em Brasilia (Kind=Unspecified), para render de
    /// data/hora local em telas/impressao. NAO usar para persistir nem comparar com
    /// colunas (use as variantes de instante/janela). Ex.: carimbo "impresso em 09/06 14:32".
    /// </summary>
    public static DateTime ConverterParaBrasilia(DateTime utc) => OperacionalFuso.ParaBrasilia(utc);

    /// <summary>"dd/MM/yyyy HH:mm" em Brasilia (pt-BR), o carimbo dos e-mails de seguranca da conta.</summary>
    public static string Formatar(DateTime utc) =>
        ConverterParaBrasilia(utc).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>"Agora" no horario de Brasilia (data + hora). Para exibicao apenas.</summary>
    public static DateTime Agora() => ConverterParaBrasilia(DateTime.UtcNow);

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
    /// Instante UTC real de uma data e hora de parede em Brasilia (ex.: inicio de janela de entrega
    /// 30/09 12:00 -> 15:00Z). Mesmo tratamento de gap/ambiguidade de <see cref="InicioRealDoDiaUtc"/>.
    /// </summary>
    public static DateTime InstanteUtc(DateOnly dia, TimeOnly hora)
    {
        var local = dia.ToDateTime(hora); // Kind=Unspecified, hora "local" de Brasilia
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
