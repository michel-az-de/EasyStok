namespace EasyStock.Application.Services.Pedidos;

public static class PlanoPagamentoEncomenda
{
    public static bool TemSinal(DateTime? agendadoParaEm, DateTime agora) =>
        agendadoParaEm is { } agendado
        && HorarioBrasil.DataOperacional(agendado) > HorarioBrasil.DataOperacional(agora);

    public static decimal Sinal(decimal total) =>
        Math.Round(total / 2m, 2, MidpointRounding.AwayFromZero);
}
