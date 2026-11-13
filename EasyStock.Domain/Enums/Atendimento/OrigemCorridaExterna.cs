namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// De onde veio a corrida externa (I9.1, ADR-0061 D5). <c>Manual</c> é a registrada por uma pessoa
/// depois de pedir pelo app do provedor; <c>Api</c> é a chamada pelo EasyStok na API do provedor (#2158, ADR-0064).
/// </summary>
public enum OrigemCorridaExterna
{
    Manual = 1,
    Api = 2
}
