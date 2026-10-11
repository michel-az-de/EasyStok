namespace EasyStock.Application.Services.Pedidos;

public static class HorizonteAgendamento
{
    public const int Dias = 14;

    public static void Validar(DateOnly data, DateOnly hoje)
    {
        if (data < hoje || data > hoje.AddDays(Dias))
            throw new UseCaseValidationException($"A data de entrega deve ser entre hoje e os próximos {Dias} dias.");
    }
}
