using EasyStock.Application.Ports.Output.Lookup;

namespace EasyStock.Infra.Integrations.Geocoding;

/// <summary>
/// Geocoding desligado (dev/CI ou flag <c>ENABLE_NOMINATIM_GEOCODING</c> off):
/// sempre <see langword="null"/>. O frete cai para a tabela de zonas (ADR-0017).
/// Evita dependência de rede em dev e em testes.
/// </summary>
public sealed class NoOpGeocodingClient : IGeocodingClient
{
    public Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default)
        => Task.FromResult<GeocodeResultado?>(null);
}
