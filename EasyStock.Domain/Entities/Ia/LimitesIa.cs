namespace EasyStock.Domain.Entities.Ia;

/// <summary>
/// #2967: limites das colunas de valor em US$ (numeric(12,6)) e do fator fora do pico (numeric(4,3)).
/// O domínio recusa o que o banco recusaria ou arredondaria em silêncio.
/// </summary>
internal static class LimitesIa
{
    public const int CasasValor = 6;
    public const int CasasFator = 3;
    public const decimal ValorMaximoExclusivo = 1_000_000m;

    public static void ValidarValor(decimal? valor, string campo)
    {
        if (valor is not { } v) return;
        if (v < 0)
            throw new RegraDeDominioVioladaException($"{campo} não pode ser negativo.");
        if (v >= ValorMaximoExclusivo || decimal.Round(v, CasasValor) != v)
            throw new RegraDeDominioVioladaException($"{campo} precisa ficar abaixo de 1.000.000 e ter até {CasasValor} casas.");
    }
}
