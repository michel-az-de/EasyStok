namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// De onde veio a corrida externa (I9.1, ADR-0061 D5). <c>Manual</c> é a registrada por uma pessoa
/// depois de pedir pelo app do provedor; a E10 acrescenta a criada pela API.
/// </summary>
public enum OrigemCorridaExterna
{
    Manual = 1
}