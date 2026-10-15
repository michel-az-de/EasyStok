using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Domain.Storefront.Frete;

namespace EasyStock.Application.UseCases.Storefront.Frete;

/// <summary>Conta do frete por raio para um destino, com a rota medida quando houve.</summary>
internal sealed record FreteRaioCotacao(FreteRaioResultado Resultado, RotaResultado? Rota);

/// <summary>
/// A conta única do frete por raio (ADR-0017): geocodifica o destino, mede a rota e aplica a
/// <see cref="FreteRaioCalculadora"/>. O checkout e o "testar um endereço" da loja chamam aqui, para
/// que os dois nunca divirjam.
/// </summary>
internal static class FreteRaioCotador
{
    /// <summary><see langword="null"/> quando o geocode não veio ou veio impreciso: o caller cai para a zona.</summary>
    public static async Task<FreteRaioCotacao?> CotarAsync(
        FreteRaioConfig config,
        GeocodeQuery destino,
        IGeocodingClient geocodingClient,
        IRotaClient rotaClient,
        CancellationToken ct)
    {
        var geo = await geocodingClient.GeocodificarAsync(destino, ct);
        if (geo is null || !geo.Confiavel)
            return null;

        // Rota real (issue #1274) quando disponível; null mantém haversine × fator.
        var rota = await rotaClient.MedirAsync(
            new RotaQuery(config.Origem.Lat, config.Origem.Lng, geo.Lat, geo.Lng), ct);

        var resultado = FreteRaioCalculadora.Calcular(
            new Coordenada(geo.Lat, geo.Lng), config, rota?.DistanciaMetros);
        return new FreteRaioCotacao(resultado, rota);
    }
}
