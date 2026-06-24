namespace EasyStock.Application.Ports.Output.Lookup;

/// <summary>
/// Endereço textual a geocodificar. Nunca recebe lat/lng do cliente (ADR-0017):
/// o backend é quem resolve a coordenada a partir do endereço.
/// </summary>
public sealed record GeocodeQuery(
    string? Logradouro,
    string? Numero,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Cep);

/// <summary>
/// Coordenada resolvida + sinal de confiança da granularidade (ADR-0017).
/// <see cref="Confiavel"/> = o ponto é granular o bastante (casa + número) para
/// cobrar frete com segurança; senão o front avisa "valor estimado".
/// </summary>
public sealed record GeocodeResultado(
    double Lat,
    double Lng,
    bool Confiavel);

/// <summary>
/// Geocoding de endereço → coordenada. Nominatim em prod, NoOp em dev/flag-off.
///
/// <para>
/// <strong>Best-effort</strong>: sem match, timeout, 4xx/5xx ou JSON inválido →
/// <see langword="null"/>. Nunca lança — encapsula falhas. O <c>CalcularFreteUseCase</c>
/// (S4) trata null caindo para o frete por zona (ADR-0017 fallback).
/// </para>
/// </summary>
public interface IGeocodingClient
{
    /// <summary>
    /// Resolve o endereço numa coordenada. <see langword="null"/> quando não
    /// resolve / provider indisponível / timeout.
    /// </summary>
    Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default);
}
