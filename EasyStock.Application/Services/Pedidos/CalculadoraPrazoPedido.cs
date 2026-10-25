namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Prazo mínimo que o atendimento pode prometer (S15, RN-06/RN-22): o preparo mais longo entre os
/// itens mais o respiro da configuração. Item sem tempo próprio usa o padrão da empresa
/// (<c>ConfiguracaoAtendimento.TempoPreparoPadraoMinutos</c>).
/// </summary>
public static class CalculadoraPrazoPedido
{
    public static int PrazoMinimo(
        IEnumerable<int?> temposPreparoMinutos,
        int tempoPreparoPadraoMinutos,
        int respiroMinutos)
    {
        ArgumentNullException.ThrowIfNull(temposPreparoMinutos);

        var maiorPreparo = temposPreparoMinutos
            .Select(t => t ?? tempoPreparoPadraoMinutos)
            .DefaultIfEmpty(0)
            .Max();

        return maiorPreparo + respiroMinutos;
    }
}
