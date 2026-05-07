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
    /// Quando <c>true</c>, registra o <c>FaturaReconciliacaoJob</c> (F6) que
    /// roda hora em hora consultando o gateway para fechar gaps de webhooks
    /// perdidos. Default false — habilitar quando IEfiPixService.GetCobrancaAsync
    /// estiver implementado (atualmente o adapter retorna Desconhecido, NO-OP).
    /// </summary>
    public bool EnableFaturaReconciliacaoJob { get; set; }

    /// <summary>
    /// Quando <c>true</c>, registra o <c>FaturaVencimentoJob</c> (F6) que roda
    /// 1x/dia (09:00 UTC) processando notificacoes D-3, D-1 e marcando faturas
    /// como Vencida no D+0+. Default true em producao — recomendado.
    /// </summary>
    public bool EnableFaturaVencimentoJob { get; set; }

    /// <summary>
    /// Quando <c>true</c>, CobrancaAssinaturaJob envia emails de cobrança/dunning
    /// diretamente via IEmailService (comportamento legado). Quando <c>false</c>
    /// (padrão), publica EventoNotificacao e o Worker despacha via Outbox.
    /// </summary>
    public bool UseLegacyEmailAlerts { get; set; } = false;
}
