namespace EasyStock.Application.Operacao;

/// <summary>
/// Sinais já agregados de um cliente, entrada do <see cref="FleetHealthScoring"/>.
/// </summary>
public readonly record struct FleetHealthSignals(
    bool Suspensa,
    int VendasHojeCount,
    DateTime? UltimaVendaEm,
    int TicketsAbertos,
    int TicketsSlaViolado,
    int FaturasVencidasCount,
    DateTime? TrialFim);

/// <summary>Resultado: banda (ok/warn/crit), severidade p/ ordenar e os motivos (chaves).</summary>
public readonly record struct FleetHealthResult(
    string Band,
    int Severidade,
    IReadOnlyList<string> Motivos);

/// <summary>
/// Avalia a situação de um cliente em PALAVRA + MOTIVOS (não um score 0-100 abstrato).
/// Puro e determinístico (recebe 'now' por parâmetro, sem relógio ambiente). A UI traduz
/// cada motivo-chave para uma frase em pt-BR com os números. Issue 623 (reescrita).
/// </summary>
public static class FleetHealthScoring
{
    public const string BandOk = "ok";
    public const string BandWarn = "warn";
    public const string BandCrit = "crit";

    public const string MotivoSuspensa = "suspensa";
    public const string MotivoFaturaVencida = "fatura-vencida";
    public const string MotivoSlaViolado = "sla-violado";
    public const string MotivoTrialVencendo = "trial-vencendo";
    public const string MotivoSemVendas = "sem-vendas";
    public const string MotivoTicketAberto = "ticket-aberto";

    /// <summary>Dias sem nenhuma venda até virar alerta.</summary>
    public const int DiasSemVenda = 7;
    /// <summary>Janela (dias) em que o fim do teste já conta como alerta.</summary>
    public const int DiasTrialVencendo = 3;

    public static FleetHealthResult Avaliar(FleetHealthSignals s, DateTime nowUtc)
    {
        var motivos = new List<string>();
        var crit = false;
        var warn = false;

        // Crítico — precisa de ação hoje.
        if (s.Suspensa) { motivos.Add(MotivoSuspensa); crit = true; }
        if (s.FaturasVencidasCount > 0) { motivos.Add(MotivoFaturaVencida); crit = true; }
        if (s.TicketsSlaViolado > 0) { motivos.Add(MotivoSlaViolado); crit = true; }

        // Atenção — fica de olho.
        if (s.TrialFim is { } fim && fim >= nowUtc && fim <= nowUtc.AddDays(DiasTrialVencendo))
        {
            motivos.Add(MotivoTrialVencendo);
            warn = true;
        }

        var semVendas = !s.Suspensa
            && (s.UltimaVendaEm is null || s.UltimaVendaEm.Value < nowUtc.AddDays(-DiasSemVenda));
        if (semVendas) { motivos.Add(MotivoSemVendas); warn = true; }

        // Ticket em aberto só vira motivo se não houver já um SLA violado (evita duplicar).
        if (s.TicketsAbertos > 0 && s.TicketsSlaViolado == 0) { motivos.Add(MotivoTicketAberto); warn = true; }

        var band = crit ? BandCrit : warn ? BandWarn : BandOk;
        var baseSev = crit ? 3 : warn ? 2 : 0;
        var severidade = baseSev * 10 + Math.Min(9, motivos.Count); // mais motivos sobe dentro da banda
        return new FleetHealthResult(band, severidade, motivos);
    }
}
