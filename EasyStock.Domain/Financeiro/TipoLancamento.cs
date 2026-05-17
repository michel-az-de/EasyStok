namespace EasyStock.Domain.Financeiro;

/// <summary>
/// Direcao do lancamento financeiro previsto/realizado.
/// "Receber" e Conta a Receber (entrada futura). "Pagar" e Conta a Pagar (saida futura).
/// </summary>
public enum TipoLancamento
{
    Receber = 1,
    Pagar = 2
}
