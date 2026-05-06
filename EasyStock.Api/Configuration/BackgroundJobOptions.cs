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
    /// Quando <c>true</c>, CobrancaAssinaturaJob envia emails de cobrança/dunning
    /// diretamente via IEmailService (comportamento legado). Quando <c>false</c>
    /// (padrão), publica EventoNotificacao e o Worker despacha via Outbox.
    /// </summary>
    public bool UseLegacyEmailAlerts { get; set; } = false;
}
