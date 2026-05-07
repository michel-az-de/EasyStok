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

    // Contas a Pagar / Contas a Receber (CAP/CAR)
    ContaPagarVencendo = 25,
    ContaPagarVencida = 26,
    ContaReceberVencendo = 27,
    ContaReceberVencida = 28,
    ParcelaRecebida = 29
}
