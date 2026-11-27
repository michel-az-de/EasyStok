using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Sales;

/// <summary>
/// Ritmo de compra do cliente (#3387): mediana de dias entre as compras dos 12 dias de compra mais recentes.
/// Regra única da Ficha e do PDF. Só recebe compras válidas (<see cref="RegraDeCompra"/>).
/// </summary>
/// <param name="MedianaDias">Dias entre uma compra e a seguinte, pela mediana.</param>
/// <param name="ComprasAnalisadas">Quantos dias de compra entraram na conta.</param>
public sealed record RitmoDeCompra(int MedianaDias, int ComprasAnalisadas)
{
    public const int MaximoDias = 12;
    public const int MinimoDias = 3;

    /// <summary>Null com menos de <see cref="MinimoDias"/> dias distintos de compra (sem ritmo para afirmar).</summary>
    public static RitmoDeCompra? Calcular(IEnumerable<DateTime> comprasUtc)
    {
        ArgumentNullException.ThrowIfNull(comprasUtc);

        // #3387: dois pedidos no mesmo dia (no fuso da loja) são uma ida só.
        var dias = comprasUtc
            .Select(d => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), OperacionalFuso.Zona).Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(MaximoDias)
            .OrderBy(d => d)
            .ToList();
        if (dias.Count < MinimoDias) return null;

        var intervalos = dias.Zip(dias.Skip(1), (a, b) => (b - a).Days).Order().ToList();
        var meio = intervalos.Count / 2;
        var mediana = intervalos.Count % 2 == 1
            ? intervalos[meio]
            : (int)Math.Round((intervalos[meio - 1] + intervalos[meio]) / 2.0, MidpointRounding.AwayFromZero);
        return new RitmoDeCompra(mediana, dias.Count);
    }
}
