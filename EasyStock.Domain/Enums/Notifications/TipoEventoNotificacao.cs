namespace EasyStock.Domain.Enums.Notifications;

public enum TipoEventoNotificacao
{
    ProdutoVencendo = 1,
    ProdutoVencido = 2,
    TarefaPendente = 3,
    ResetSenha = 4,
    AssinaturaExpirando = 5,
    AssinaturaExpirada = 6,
    BroadcastSuperAdmin = 7,
    ConfirmacaoEmail = 8,
    AlertaEstoqueCritico = 9,
    TicketCriado = 10,
    TicketRespondidoCliente = 11,
    TicketRespondidoAdmin = 12,
    TicketStatusAlterado = 13,
    TicketAtribuido = 14,
    TicketEncaminhadoNivel = 15,
    SlaProximoVencer = 16,
    SlaViolado = 17,
    BugFixCriado = 18,

    // Modulo Financeiro (F5)
    FaturaCriada = 19,
    FaturaVencendo = 20,
    FaturaPaga = 21,
    FaturaVencida = 22,
    PagamentoConfirmado = 23,
    PagamentoFalhou = 24,

    // Modulo Helpdesk — convite CSAT pos fechamento
    ConviteCsat = 25,

    // Pedido agendado (mobile) — lembretes disparados pelo AgendamentoNotificacaoService.
    PedidoAgendadoHoje = 26,
    PedidoAgendadoEm1Hora = 27,
    PedidoAgendadoEm10Minutos = 28,

    // Módulo de Relatórios (PR-C0)
    RelatorioPronto = 29,
    RelatorioFalhou = 30,
    RelatorioExpirado = 31,

    // Contas a Pagar / Contas a Receber (CAP/CAR)
    ContaPagarVencendo = 32,
    ContaPagarVencida = 33,
    ContaReceberVencendo = 34,
    ContaReceberVencida = 35,
    ParcelaRecebida = 36,

    // Caixa esquecido aberto de um dia anterior (CaixaEsquecidoJob — só notifica, não fecha).
    CaixaAbertoEsquecido = 37,

    // Avisos de status do pedido ao cliente pelo WhatsApp (S13). 42-45 seguem reservados pelo plano
    // (docs/plan/atendimento-whatsapp: avaliacao, reembolso, campanhas).
    PedidoPagoConfirmado = 38,
    PedidoEmPreparo = 39,
    PedidoSaiuParaEntrega = 40,
    PedidoEntregue = 41,

    // Atendimento por WhatsApp (S07): o agente passou a conversa para a dona.
    ConversaEscalada = 46,

    // Avaliacao de um toque (S26): pedido de avaliacao ao cliente 30 min apos a entrega.
    AvaliacaoSolicitada = 42,

    // Lembretes da dona (S43): lembrete venceu (manual) ou nasceu do avaliador (pagamento sem baixa,
    // cliente sem resposta). Push para quem e o lembrete, ou para a empresa toda.
    LembreteVencido = 47
}
