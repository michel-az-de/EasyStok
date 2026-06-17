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
    public bool EnableFaturaVencimentoJob { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, CobrancaAssinaturaJob envia emails de cobrança/dunning
    /// diretamente via IEmailService (comportamento legado). Quando <c>false</c>
    /// (padrão), publica EventoNotificacao e o Worker despacha via Outbox.
    /// </summary>
    public bool UseLegacyEmailAlerts { get; set; } = false;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>ContaFinanceiraVencimentoJob</c> (CAP/CAR)
    /// que roda 1x/dia (09:30 UTC) marcando parcelas de Contas a Pagar/Receber
    /// como vencidas e atualizando status agregado. Default true em producao.
    /// </summary>
    public bool EnableContaFinanceiraVencimentoJob { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>ContaReceberPixReconciliacaoJob</c>
    /// que roda hora em hora consultando Efi pra fechar gaps de webhook em
    /// parcelas CR com Pix ativo. Default true em producao.
    /// </summary>
    public bool EnableContaReceberPixReconciliacaoJob { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>CaixaEsquecidoJob</c> que roda 1x/dia (10:00 UTC ≈
    /// 07:00 BRT) detectando caixas abertos não fechados de dias anteriores e notificando in-app
    /// (só notifica, não fecha — ADR-0034 / issue #641). Default true em producao.
    /// </summary>
    public bool EnableCaixaEsquecidoJob { get; set; } = true;
}
