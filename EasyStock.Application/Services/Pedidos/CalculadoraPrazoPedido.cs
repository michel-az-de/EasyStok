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

    /// <summary>
    /// Corte da S16 (RN-21): a janela que começa em <paramref name="data"/> às <paramref name="horaInicio"/>
    /// (fuso da loja, <see cref="HorarioBrasil"/>) só serve se não for antes de agora + prazo. Usado na
    /// listagem de janelas e na revalidação do checkout, para os dois cortarem igual.
    /// </summary>
    public static bool AtendePrazo(DateOnly data, TimeOnly horaInicio, DateTime agoraUtc, int prazoMinimoMinutos)
    {
        var limite = HorarioBrasil.ConverterParaBrasilia(agoraUtc).AddMinutes(prazoMinimoMinutos);
        return data.ToDateTime(horaInicio) >= limite;
    }

    /// <summary>
    /// #1506: a janela de <paramref name="data"/> já terminou (fim no passado, fuso da loja). Vale mesmo
    /// sem prazo mínimo (site): antes o site listava e aceitava janela de hoje já encerrada ou de data
    /// passada, e o cliente pagava por um horário que não existia mais.
    /// </summary>
    public static bool JanelaJaPassou(DateOnly data, TimeOnly horaFim, DateTime agoraUtc)
        => data.ToDateTime(horaFim) <= HorarioBrasil.ConverterParaBrasilia(agoraUtc);
}
