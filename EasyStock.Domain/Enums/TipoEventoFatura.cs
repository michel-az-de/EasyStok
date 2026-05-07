namespace EasyStock.Domain.Enums;

/// <summary>
/// Tipos de evento registrados em <see cref="Entities.FaturaEvento"/> — audit
/// trail completo de uma <see cref="Entities.Fatura"/>. Padrao analogo a
/// <see cref="TicketAcaoHistorico"/>.
/// </summary>
public enum TipoEventoFatura
{
    Criada,
    ItemAdicionado,
    ItemRemovido,
    Emitida,
    PagamentoRegistrado,
    PagamentoConfirmado,
    PagamentoFalhou,
    PagamentoEstornoSolicitado,
    PagamentoEstornado,
    StatusAlterado,
    Vencida,
    Cancelada,
    ReenviadaCliente,
    PdfGerado,
    TicketRelacionadoVinculado,

    // F6 — Notificacoes de vencimento (anti-duplicacao via FaturaEvento)
    NotificadaVencendoD3,
    NotificadaVencendoD1,

    // F6 — Reconciliacao gateway
    ReconciliacaoConsultouGateway,
    ReconciliacaoFechouGap
}
