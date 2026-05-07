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
    TicketRelacionadoVinculado
}
