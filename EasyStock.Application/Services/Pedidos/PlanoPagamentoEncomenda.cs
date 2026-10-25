namespace EasyStock.Application.Services.Pedidos;

public static class PlanoPagamentoEncomenda
{
    public static bool TemSinal(DateTime? agendadoParaEm, DateTime agora) =>
        agendadoParaEm is { } agendado
        && HorarioBrasil.DataOperacional(agendado) > HorarioBrasil.DataOperacional(agora);

    public static decimal Sinal(decimal total) =>
        Math.Round(total / 2m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// #2018: quanto do sinal ainda falta receber: o sinal do <paramref name="total"/> menos o que já foi pago
    /// (<paramref name="total"/> - <paramref name="saldo"/>), sem passar do saldo nem ficar negativo.
    /// </summary>
    public static decimal SinalRestante(decimal total, decimal saldo) =>
        Math.Min(saldo, Math.Max(0, Sinal(total) - (total - saldo)));
}
