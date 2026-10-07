namespace EasyStock.Api.Configuration;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    // Jobs fixos (default true para manter comportamento atual)
    public bool EnableAnalisadorEstoque { get; set; } = true;
    public bool EnableCacheWarmup { get; set; } = true;
    public bool EnableHealthSnapshot { get; set; } = true;
    public bool EnableLogStorage { get; set; } = true;

    /// <summary>S39: disparador das mensagens programadas ao cliente. Desligar é o rollback.</summary>
    public bool EnableMensagensProgramadas { get; set; } = true;

    /// <summary>S57: reenvio automático das mensagens com falha temporária. Desligar é o rollback.</summary>
    public bool EnableReenvioMensagens { get; set; } = true;

    /// <summary>
    /// #1432: leitura das caixas de suporte (IMAP) das empresas que configuraram uma. Sem caixa configurada não
    /// conecta em nada. Desligar é o rollback da entrada de e-mail.
    /// </summary>
    public bool EnableAtendimentoCaixaEmail { get; set; } = true;

    /// <summary>S43: avaliador dos lembretes da dona. Desligar é o rollback.</summary>
    public bool EnableAvaliadorLembretes { get; set; } = true;

    /// <summary>
    /// S30: job das campanhas (primeira onda no horário, conciliação com o outbox, encerramento e
    /// lembrete). Desligar é o rollback: as campanhas ficam <c>Agendada</c>.
    /// </summary>
    public bool EnableCampanhaJob { get; set; } = true;

    // Jobs opcionais (default false)
    public bool EnableAlertasEstoqueJob { get; set; }
    public bool EnableProcessarRecebimentoJob { get; set; }
    public bool EnableRecalcularVelocidadesJob { get; set; }
    public bool EnableRelatorioMensalJob { get; set; }
    public bool EnableDiagnosticoEmailReport { get; set; }
    /// <summary>
    /// Quando <c>true</c>, registra o <c>ContaFinanceiraVencimentoJob</c> (CAP/CAR)
    /// que roda 1x/dia (09:30 UTC) marcando parcelas de Contas a Pagar/Receber
    /// como vencidas e atualizando status agregado. Default true em producao.
    /// </summary>
    public bool EnableContaFinanceiraVencimentoJob { get; set; } = true;

    /// <summary>
    /// N12 (Q3, decisão do Felipe): quando <c>true</c>, o <c>ContaFinanceiraVencimentoJob</c> publica os avisos de conta a
    /// pagar e a receber vencendo (D-3, D-1) e vencida. Padrão <c>false</c>: o job só marca a parcela vencida, sem evento e
    /// sem carimbar o dedup, porque o aviso nasceria sem rotina semeada e sem destinatário no payload e o carimbo faria o
    /// aviso sumir para sempre. Religar é <c>BackgroundJobs:EnableContaFinanceiraNotificacoes=true</c>.
    /// </summary>
    public bool EnableContaFinanceiraNotificacoes { get; set; }

    /// <summary>
    /// Quando <c>true</c>, registra o <c>ContaReceberPixReconciliacaoJob</c>
    /// que roda hora em hora consultando Efi pra fechar gaps de webhook em
    /// parcelas CR com Pix ativo. Default true em producao.
    /// </summary>
    public bool EnableContaReceberPixReconciliacaoJob { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>CobrancaPedidoJob</c> (S11) que roda a cada 60 s expirando
    /// links do Mercado Pago vencidos: reemite uma vez para pedido da conversa e cancela o resto
    /// (<c>BackgroundJobs:EnableCobrancaPedido</c>). Default true; <c>false</c> é o rollback do job.
    /// </summary>
    public bool EnableCobrancaPedido { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>ImpressaoPendenteAlertaJob</c> (S20): a cada 2 min publica
    /// <c>impressao.atrasada</c> para canhoto pendente há mais de 3 min
    /// (<c>BackgroundJobs:EnableImpressaoPendenteAlerta</c>). Default true; <c>false</c> desliga o alerta.
    /// </summary>
    public bool EnableImpressaoPendenteAlerta { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>PedidoAtrasoJob</c> (S21) que roda a cada 60 s publicando
    /// <c>pedido.atrasado</c> uma vez por pedido aguardando com o início previsto vencido
    /// (<c>BackgroundJobs:EnablePedidoAtraso</c>). Default true; <c>false</c> é o rollback do job.
    /// </summary>
    public bool EnablePedidoAtraso { get; set; } = true;

    /// <summary>
    /// Quando <c>true</c>, registra o <c>CaixaEsquecidoJob</c> que roda 1x/dia (10:00 UTC ≈
    /// 07:00 BRT) detectando caixas abertos não fechados de dias anteriores e notificando in-app
    /// (só notifica, não fecha — ADR-0034 / issue #641). Default true em producao.
    /// </summary>
    public bool EnableCaixaEsquecidoJob { get; set; } = true;

    /// <summary>
    /// Drena a fila em memória do turno do agente de atendimento (S06). Roda na Api porque o webhook
    /// que enfileira está aqui e <c>BackgroundQueueService</c> não atravessa processos.
    /// </summary>
    public bool EnableAtendimentoTurnoAgente { get; set; } = true;

    /// <summary>Apaga, de hora em hora, as sessões do chat do site vencidas há mais de um dia (S36).</summary>
    public bool EnableLimpezaSessoesChatSite { get; set; } = true;
}
