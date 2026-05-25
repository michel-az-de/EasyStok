namespace EasyStock.Domain.Enums.Financeiro;

public enum TipoEventoContaFinanceira
{
    Criada = 0,
    Emitida = 1,
    ParcelaAdicionada = 2,
    ParcelaRemovida = 3,
    PagamentoRegistrado = 4,
    PagamentoConfirmado = 5,
    PagamentoEstornado = 6,
    ParcelaVencida = 7,
    NotificadaD3 = 8,
    NotificadaD1 = 9,
    NotificadaVencida = 10,
    PixGerado = 11,
    PixLimpado = 12,
    PixReconciliado = 13,
    Cancelada = 14
}
