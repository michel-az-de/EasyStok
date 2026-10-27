namespace EasyStock.Api.Configuration;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    // Jobs fixos (default true para manter comportamento atual)

    /// <summary>
    /// #2016: alertas de estoque no sino (validade próxima e vencida, abaixo do mínimo, reposição), por empresa, de hora
    /// em hora. Ligado por padrão e em produção; <c>false</c> é o rollback.
    /// </summary>
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
    /// Nasce DESLIGADO (#1642): a leitura usa só UNSEEN, sem data de corte. Ligar com a caixa real importa
    /// todo o acumulado não lido como conversa e marca tudo como lido na caixa, sem volta.
    /// </summary>
    public bool EnableAtendimentoCaixaEmail { get; set; }

    /// <summary>S43: avaliador dos lembretes da dona. Desligar é o rollback.</summary>
    public bool EnableAvaliadorLembretes { get; set; } = true;

    /// <summary>F16 (#1246): vigia das integrações, a cada 15 min. Desligar é o rollback.</summary>
    public bool EnableVigiaIntegracoes { get; set; } = true;

    /// <summary>
    /// S30: job das campanhas (primeira onda no horário, conciliação com o outbox, encerramento e
    /// lembrete). Desligar é o rollback: as campanhas ficam <c>Agendada</c>.
    /// </summary>
    public bool EnableCampanhaJob { get; set; } = true;

    // Jobs opcionais (default false)
    public bool EnableProcessarRecebimentoJob { get; set; }

    /// <summary>
    /// #2016: recálculo da velocidade de saída dos lotes, por empresa, a cada 6 h. Padrão <c>false</c> no código; o
    /// <c>appsettings.Production.json</c> liga (e o guard de arquitetura exige a chave lá). <c>false</c> é o rollback.
    /// </summary>
    public bool EnableRecalcularVelocidadesJob { get; set; }
    public bool EnableRelatorioMensalJob { get; set; }
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
    /// Vigia da operação (#2096): pedido pronto sem entregador, encomenda chegando ou sem confirmação e resumo do freezer
    /// (<c>BackgroundJobs:EnableVigiaOperacao</c>). Default true; <c>false</c> é o rollback do job.
    /// </summary>
    public bool EnableVigiaOperacao { get; set; } = true;

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

/// <summary>
/// Deduplicação e retenção dos alertas automáticos de estoque (#2016), seção <c>BackgroundJobs:AlertasEstoque</c>.
/// O alerta é por condição, não por dia: enquanto a condição de um item continua, o sino não recebe outro alerta do mesmo
/// tipo e item se o anterior ainda não foi lido ou é recente.
/// </summary>
public sealed class AlertasEstoqueOptions
{
    public const string SectionName = "BackgroundJobs:AlertasEstoque";

    /// <summary>
    /// Janela, em dias, em que um alerta lido ainda segura o seguinte do mesmo tipo e item (não lido segura sempre, até a
    /// retenção). Se a condição se resolve e volta, o alerta nasce de novo mesmo dentro da janela. Padrão 7.
    /// </summary>
    public int DiasSemRepetir { get; set; } = 7;

    /// <summary>
    /// Dias até o alerta sair do sino em qualquer estado. O alerta lido sai assim que passa da janela
    /// <see cref="DiasSemRepetir"/> (antes disso ele ainda segura a repetição). Padrão 30.
    /// </summary>
    public int RetencaoDias { get; set; } = 30;
}
