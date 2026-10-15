namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Normaliza instantes para UTC sem ler relógio ambiente.</summary>
internal static class Datas
{
    public static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
