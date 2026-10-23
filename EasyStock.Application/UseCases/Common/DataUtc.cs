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
    /// Data pura (<c>2026-10-01</c>, Kind Unspecified à meia-noite) é o dia civil de Brasília: vira 03:00Z.
    /// Instante (ISO com hora ou "Z") só é normalizado para UTC.
    /// </summary>
    public static DateTime? InicioDoPeriodoUtc(DateTime? desde)
    {
        if (!desde.HasValue) return null;
        return DataPura(desde.Value)
            ? HorarioBrasil.InicioRealDoDiaUtc(DateOnly.FromDateTime(desde.Value))
            : ParaUtc(desde.Value);
    }

    /// <summary>
    /// Fim de filtro por período (<c>ate</c>), inclusivo, para <c>coluna &lt;= valor</c>. Data pura inclui o dia
    /// inteiro de Brasília (até 23:59:59.999999, a precisão do timestamptz); sem isso <c>ate=2026-10-23</c> virava
    /// 23/10 00:00 e escondia o último dia. Instante só é normalizado para UTC.
    /// </summary>
    public static DateTime? FimDoPeriodoUtc(DateTime? ate)
    {
        if (!ate.HasValue) return null;
        return DataPura(ate.Value)
            ? HorarioBrasil.InicioRealDoDiaUtc(DateOnly.FromDateTime(ate.Value).AddDays(1)).AddTicks(-10)
            : ParaUtc(ate.Value);
    }

    private static bool DataPura(DateTime d) => d.Kind == DateTimeKind.Unspecified && d.TimeOfDay == TimeSpan.Zero;
}
