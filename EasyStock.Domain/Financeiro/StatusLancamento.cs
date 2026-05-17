namespace EasyStock.Domain.Financeiro;

/// <summary>
/// Ciclo de vida do lancamento. Pendente: nada baixado. Parcial: alguma baixa
/// mas sobra valor. Quitado: total baixado. Cancelado: anulado, nao admite
/// novas baixas.
/// </summary>
public enum StatusLancamento
{
    Pendente = 1,
    Parcial = 2,
    Quitado = 3,
    Cancelado = 4
}
