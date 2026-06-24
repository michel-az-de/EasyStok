namespace EasyStock.Domain.Storefront.Frete;

/// <summary>Coordenada geográfica em graus decimais.</summary>
public readonly record struct Coordenada(double Lat, double Lng);

/// <summary>
/// Uma faixa de distância → preço. Cobre distâncias de rota até
/// <see cref="AteMetros"/> (inclusive). Ordenadas por <see cref="AteMetros"/>.
/// </summary>
public sealed record FreteFaixa(string Id, int AteMetros, int ValorCentavos);

/// <summary>
/// Config de frete por raio do storefront (ADR-0017). A <see cref="Origem"/> é o
/// ponto fixo da cozinha. Persistência fica fora desta classe (S3).
/// </summary>
public sealed record FreteRaioConfig(
    Coordenada Origem,
    double FatorRota,
    int FaixaGratisMetros,
    int RaioMaxMetros,
    IReadOnlyList<FreteFaixa> Faixas);

/// <summary>Resultado do cálculo de frete por raio. Espelha o contrato do ADR-0017.</summary>
public sealed record FreteRaioResultado(
    int DistanciaMetros,
    int DistanciaRotaMetros,
    string? FaixaId,
    int ValorCentavos,
    bool Gratis,
    bool ForaDeCobertura);

/// <summary>
/// Frete por raio (ADR-0017, supersede o ADR-0011 no cálculo):
/// <c>distanciaRota = haversine(origem, destino) × FATOR_ROTA</c>, depois faixa por
/// distância. Pura — sem DB, sem geocoding, sem I/O. Geocoding (S2) e persistência
/// da config (S3) ficam fora; aqui só a aritmética do raio.
///
/// <para>
/// "Fora de cobertura" (acima do raio máximo) é resultado de NEGÓCIO, não erro: o
/// caller responde 200 e o front oferece retirada.
/// </para>
/// </summary>
public static class FreteRaioCalculadora
{
    private const double RaioTerraMetros = 6_371_000.0;

    /// <summary>Calcula o frete do destino até a origem da config.</summary>
    public static FreteRaioResultado Calcular(Coordenada destino, FreteRaioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var distanciaMetros = (int)Math.Round(
            HaversineMetros(config.Origem, destino), MidpointRounding.AwayFromZero);
        var distanciaRotaMetros = (int)Math.Round(
            distanciaMetros * config.FatorRota, MidpointRounding.AwayFromZero);

        // Acima do raio máximo → retirada (negócio, não erro).
        if (distanciaRotaMetros > config.RaioMaxMetros)
            return new FreteRaioResultado(
                distanciaMetros, distanciaRotaMetros,
                FaixaId: null, ValorCentavos: 0, Gratis: false, ForaDeCobertura: true);

        // Perto da cozinha → grátis.
        if (distanciaRotaMetros <= config.FaixaGratisMetros)
            return new FreteRaioResultado(
                distanciaMetros, distanciaRotaMetros,
                FaixaId: "gratis", ValorCentavos: 0, Gratis: true, ForaDeCobertura: false);

        // Primeira faixa cuja cobertura alcança a distância de rota.
        var faixa = config.Faixas
            .OrderBy(f => f.AteMetros)
            .FirstOrDefault(f => distanciaRotaMetros <= f.AteMetros);

        // Config incompleta (nenhuma faixa cobre, mas dentro do raio) → trata como fora,
        // nunca cobra um valor inventado.
        if (faixa is null)
            return new FreteRaioResultado(
                distanciaMetros, distanciaRotaMetros,
                FaixaId: null, ValorCentavos: 0, Gratis: false, ForaDeCobertura: true);

        return new FreteRaioResultado(
            distanciaMetros, distanciaRotaMetros,
            FaixaId: faixa.Id, ValorCentavos: faixa.ValorCentavos,
            Gratis: false, ForaDeCobertura: false);
    }

    /// <summary>Distância em linha reta (metros) entre dois pontos — fórmula de Haversine.</summary>
    public static double HaversineMetros(Coordenada a, Coordenada b)
    {
        var dLat = ToRadianos(b.Lat - a.Lat);
        var dLng = ToRadianos(b.Lng - a.Lng);
        var lat1 = ToRadianos(a.Lat);
        var lat2 = ToRadianos(b.Lat);

        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
              + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return RaioTerraMetros * c;
    }

    private static double ToRadianos(double graus) => graus * Math.PI / 180.0;
}
