namespace EasyStock.Api.Configuration;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    // Jobs fixos (default true para manter comportamento atual)
    public bool EnableAnalisadorEstoque { get; set; } = true;
    public bool EnableCacheWarmup { get; set; } = true;
    public bool EnableHealthSnapshot { get; set; } = true;
    public bool EnableLogStorage { get; set; } = true;

    // Jobs opcionais (default false)
    public bool EnableAlertasEstoqueJob { get; set; }
    public bool EnableProcessarRecebimentoJob { get; set; }
    public bool EnableRecalcularVelocidadesJob { get; set; }
    public bool EnableRelatorioMensalJob { get; set; }
    public bool EnableDiagnosticoEmailReport { get; set; }
    public bool EnableCobrancaAssinaturaJob { get; set; }

    /// <summary>
    /// Quando <c>true</c>, registra o <c>FaturaBackfillJob</c> que faz uma rodada
    /// unica para gerar Fatura para CobrancaAssinatura historicas (anteriores a F5).
    /// Default false — habilitar via env var apenas durante migracao controlada.
    /// </summary>
    public bool EnableFaturaBackfillJob { get; set; }

    /// <summary>
    /// Quando <c>true</c>, CobrancaAssinaturaJob envia emails de cobrança/dunning
    /// diretamente via IEmailService (comportamento legado). Quando <c>false</c>
    /// (padrão), publica EventoNotificacao e o Worker despacha via Outbox.
    /// </summary>
    public bool UseLegacyEmailAlerts { get; set; } = false;
}
