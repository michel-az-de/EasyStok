namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Normaliza <see cref="DateTime"/> para UTC antes de persistir.
///
/// <para>
/// O Postgres <c>timestamp with time zone</c> (timestamptz), via Npgsql, rejeita
/// valores com <see cref="DateTimeKind.Unspecified"/> lançando
/// "Cannot write DateTime with Kind=Unspecified". Datas que chegam do cliente
/// (JSON desserializado) vêm justamente com Kind=Unspecified — então TODO ponto
/// de fronteira command→entidade que grava uma data deve passar por aqui.
/// </para>
///
/// <para>
/// Centraliza o que antes era um helper privado duplicado em CriarPedido/
/// CriarContaPagar/CriarContaReceber — e que faltava nos caminhos de edição,
/// parcela e pagamento (mesma classe de bug de 500 ao salvar).
/// </para>
/// </summary>
public static class DataUtc
{
    /// <summary>Converte para UTC: Utc fica igual, Local converte, Unspecified é tratado como UTC.</summary>
    public static DateTime ParaUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    /// <summary>Versão para datas opcionais — null permanece null.</summary>
    public static DateTime? ParaUtcOpcional(DateTime? d) => d.HasValue ? ParaUtc(d.Value) : null;

    /// <summary>
    /// Início de filtro por período (<c>desde</c> da query string), pronto para comparar com coluna timestamptz.
    /// Sem offset (Kind Unspecified) é horário de Brasília: data pura vira 03:00Z e data com hora vira o instante
    /// correspondente. Utc e Local (ISO com "Z") só são normalizados para UTC.
    /// </summary>
    public static DateTime? InicioDoPeriodoUtc(DateTime? desde)
    {
        if (!desde.HasValue) return null;
        return desde.Value.Kind == DateTimeKind.Unspecified ? DeBrasiliaParaUtc(desde.Value) : ParaUtc(desde.Value);
    }

    /// <summary>
    /// Fim de filtro por período (<c>ate</c>), inclusivo, para <c>coluna &lt;= valor</c>. Data pura inclui o dia
    /// inteiro de Brasília (até 23:59:59.999999, a precisão do timestamptz); sem isso <c>ate=2026-10-23</c> virava
    /// 23/10 00:00 e escondia o último dia. Data com hora sem offset é horário de Brasília; Utc e Local só normalizam.
    /// </summary>
    public static DateTime? FimDoPeriodoUtc(DateTime? ate)
    {
        if (!ate.HasValue) return null;
        var d = ate.Value;
        if (d.Kind != DateTimeKind.Unspecified) return ParaUtc(d);
        if (!DataPura(d)) return DeBrasiliaParaUtc(d);
        var dia = DateOnly.FromDateTime(d);
        // Último dia representável: não há "dia seguinte" para somar.
        return dia == DateOnly.MaxValue ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : HorarioBrasil.InicioRealDoDiaUtc(dia.AddDays(1)).AddTicks(-10);
    }

    /// <summary>
    /// Início de filtro por período numa coluna de DATA CIVIL (ex.: <c>DataVencimento</c>), para <c>coluna &gt;= valor</c>.
    /// O dia civil vem de <see cref="InicioDoPeriodoUtc"/> (data pura é o dia de Brasília; ISO com "Z" é o instante) e
    /// vira a meia-noite UTC desse dia: a coluna guarda a data civil como 00:00Z (legado) ou 03:00Z (console), e as
    /// duas formas caem no mesmo dia UTC (#2095, F4).
    /// </summary>
    public static DateTime? InicioDoDiaCivilUtc(DateTime? desde)
    {
        var inicio = InicioDoPeriodoUtc(desde);
        if (!inicio.HasValue) return null;
        if (inicio.Value <= DateTime.MinValue.AddDays(1)) return inicio;
        return HorarioBrasil.CivilComoInstanteUtc(HorarioBrasil.DataOperacional(inicio.Value));
    }

    /// <summary>
    /// Fim de filtro por período numa coluna de DATA CIVIL, inclusivo (<c>coluna &lt;= valor</c>): o último instante do
    /// dia UTC que representa o dia civil de <see cref="FimDoPeriodoUtc"/>. Ver <see cref="InicioDoDiaCivilUtc"/>.
    /// </summary>
    public static DateTime? FimDoDiaCivilUtc(DateTime? ate)
    {
        var fim = FimDoPeriodoUtc(ate);
        if (!fim.HasValue) return null;
        if (fim.Value >= DateTime.MaxValue.AddDays(-2)) return fim;
        var dia = HorarioBrasil.DataOperacional(fim.Value);
        return HorarioBrasil.CivilComoInstanteUtc(dia.AddDays(1)).AddTicks(-10);
    }

    /// <summary>Instante UTC de uma data/hora de parede em Brasília; os extremos do <see cref="DateTime"/> saturam em vez de estourar.</summary>
    private static DateTime DeBrasiliaParaUtc(DateTime d)
    {
        if (d <= DateTime.MinValue.AddDays(1)) return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        if (d >= DateTime.MaxValue.AddDays(-1)) return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        return HorarioBrasil.InstanteUtc(DateOnly.FromDateTime(d), TimeOnly.FromTimeSpan(d.TimeOfDay));
    }

    private static bool DataPura(DateTime d) => d.Kind == DateTimeKind.Unspecified && d.TimeOfDay == TimeSpan.Zero;
}
