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
    /// perdidos. F11 implementou <c>IEfiPixService.ConsultarCobrancaAsync</c>
    /// e o job agora funciona ponta-a-ponta para Pix.
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

    /// <summary>
    /// NFC-e: configuração do job de retransmissão de notas em contingência.
    /// </summary>
    public NfceJobsOptions Nfce { get; set; } = new();
}

public sealed class NfceJobsOptions
{
    /// <summary>
    /// Liga o <c>ReprocessarContingenciaJob</c>. Em ambientes onde o
    /// gateway Focus ainda não está configurado, manter <c>false</c>.
    /// </summary>
    public bool ReprocessarContingenciaEnabled { get; set; } = false;

    public TimeSpan ReprocessarContingenciaPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quantidade máxima de notas processadas por rodada. Evita backlog
    /// segurar o job mais que o intervalo.
    /// </summary>
    public int ReprocessarContingenciaBatchSize { get; set; } = 50;

    /// <summary>
    /// Liga o <c>RenovacaoCertificadoA1Job</c> que avisa o dono da empresa
    /// 30/15/7/3 dias antes da expiração do certificado A1.
    /// </summary>
    public bool RenovacaoCertificadoEnabled { get; set; } = false;
}
