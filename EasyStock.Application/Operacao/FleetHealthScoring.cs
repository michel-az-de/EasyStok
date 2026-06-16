namespace EasyStock.Application.Operacao;

/// <summary>
/// Sinais operacionais ja agregados de uma loja ATIVA, entrada do
/// <see cref="FleetHealthScoring"/>.
/// </summary>
public readonly record struct FleetHealthSignals(
    int VendasCount,
    int PedidosTravados,
    int DevicesAtivos,
    int DevicesTotal,
    int TicketsSlaViolado,
    bool FaturaVencida,
    DateTime? TrialFim);

/// <summary>Resultado do score: 0..100, banda (ok/warn/crit) e flags de risco.</summary>
public readonly record struct FleetHealthResult(
    int Score,
    string Band,
    IReadOnlyList<string> Flags);

/// <summary>
/// Health Score por loja — feature-ancora do Centro de Comando da Frota (issue 623).
/// Puro e deterministico: recebe sinais ja agregados + o instante 'now' (sem relogio
/// ambiente, AmbientClockBan). Escopo = loja ATIVA, entao nao ha colisao com status
/// de assinatura (suspensa/cancelada ficam fora do board). Comeca em 100 e subtrai
/// penalidades; a banda deriva do score.
/// </summary>
public static class FleetHealthScoring
{
    public const string BandOk = "ok";
    public const string BandWarn = "warn";
    public const string BandCrit = "crit";

    public const string FlagDevicesOffline = "devices-offline";
    public const string FlagSemVendas = "sem-vendas-hoje";
    public const string FlagPedidosTravados = "pedidos-travados";
    public const string FlagSlaViolado = "sla-violado";
    public const string FlagFaturaVencida = "fatura-vencida";
    public const string FlagTrialVencendo = "trial-vencendo";

    /// <summary>Score &lt; este valor = "em risco" (banda warn ou crit). Definicao unica
    /// usada pelo tile, chip, cohorte e total da frota.</summary>
    public const int LimiarRisco = 70;
    private const int LimiarCrit = 40;
    private const int DiasTrialVencendo = 3;

    public static FleetHealthResult Compute(FleetHealthSignals s, DateTime nowUtc)
    {
        var score = 100;
        var flags = new List<string>();

        // Frota offline: tem device pareado mas nenhum ativo — sinal mais forte.
        if (s.DevicesTotal > 0 && s.DevicesAtivos == 0)
        {
            score -= 35;
            flags.Add(FlagDevicesOffline);
        }

        // Sem vendas no dia (loja ativa).
        if (s.VendasCount == 0)
        {
            score -= 15;
            flags.Add(FlagSemVendas);
        }

        // Pedidos travados — escalonado, com teto de penalidade.
        if (s.PedidosTravados > 0)
        {
            score -= Math.Min(20, 5 + s.PedidosTravados * 5);
            flags.Add(FlagPedidosTravados);
        }

        // SLA de ticket violado.
        if (s.TicketsSlaViolado > 0)
        {
            score -= 15;
            flags.Add(FlagSlaViolado);
        }

        // Fatura vencida.
        if (s.FaturaVencida)
        {
            score -= 15;
            flags.Add(FlagFaturaVencida);
        }

        // Trial vencendo nos proximos dias (ainda no futuro).
        if (s.TrialFim is { } fim && fim >= nowUtc && fim <= nowUtc.AddDays(DiasTrialVencendo))
        {
            score -= 10;
            flags.Add(FlagTrialVencendo);
        }

        score = Math.Clamp(score, 0, 100);
        var band = score >= LimiarRisco ? BandOk : score >= LimiarCrit ? BandWarn : BandCrit;
        return new FleetHealthResult(score, band, flags);
    }
}
