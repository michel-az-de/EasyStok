namespace EasyStock.Application.Services.Pedidos;

public static class PlanoPagamentoEncomenda
{
    /// <summary>Antecedência mínima, em dias da data de Brasília, para a encomenda cobrar o sinal automático.</summary>
    public const int DiasDeAntecedenciaDoSinal = 2;

    /// <summary>
    /// #2259: só a encomenda com <see cref="DiasDeAntecedenciaDoSinal"/> dias ou mais de antecedência cobra o sinal
    /// de 50%. Pedido para o mesmo dia ou para o dia seguinte paga o valor integral.
    /// </summary>
    public static bool TemSinal(DateTime? agendadoParaEm, DateTime agora) =>
        agendadoParaEm is { } agendado
        && HorarioBrasil.DataOperacional(agendado)
            >= HorarioBrasil.DataOperacional(agora).AddDays(DiasDeAntecedenciaDoSinal);

    public static decimal Sinal(decimal total) =>
        Math.Round(total / 2m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// #2018: quanto do sinal ainda falta receber: o sinal do <paramref name="total"/> menos o que já foi pago
    /// (<paramref name="total"/> - <paramref name="saldo"/>), sem passar do saldo nem ficar negativo.
    /// </summary>
    public static decimal SinalRestante(decimal total, decimal saldo) =>
        Math.Min(saldo, Math.Max(0, Sinal(total) - (total - saldo)));
}
