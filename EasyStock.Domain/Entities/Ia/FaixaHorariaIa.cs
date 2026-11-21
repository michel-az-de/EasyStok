namespace EasyStock.Domain.Entities.Ia;

/// <summary>
/// #3064 (S2b de #2948, D7): a faixa de preço da chamada. Pico de 01h a 04h e de 06h a 10h UTC, de segunda a sexta,
/// como a DeepSeek documenta; fora disso vale <see cref="ModeloIa.FatorForaPico"/> (1 para quem não tem faixa).
/// </summary>
public static class FaixaHorariaIa
{
    public static bool ForaDoPico(DateTime instante)
    {
        var utc = instante.Kind == DateTimeKind.Local ? instante.ToUniversalTime() : instante;
        if (utc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return true;
        var hora = utc.Hour;
        var pico = hora is >= 1 and < 4 or >= 6 and < 10;
        return !pico;
    }
}
