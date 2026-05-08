namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Ciclo de vida de uma NF-e/NFC-e. Estados terminais (sem saída):
/// <see cref="Cancelada"/>, <see cref="Rejeitada"/>, <see cref="Denegada"/>,
/// <see cref="Inutilizada"/>. Transições válidas definidas em
/// <see cref="EasyStock.Domain.Sales.NotaFiscalStateMachine"/>.
/// </summary>
public enum StatusNotaFiscal
{
    EmEmissao,
    Autorizada,
    Rejeitada,
    Denegada,
    EmContingencia,
    CancelamentoEmAndamento,
    Cancelada,
    Inutilizada,
}
