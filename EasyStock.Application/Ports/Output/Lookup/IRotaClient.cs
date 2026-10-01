namespace EasyStock.Application.Ports.Output.Lookup;

/// <summary>Trecho a medir: da origem (cozinha) ao destino (cliente), em graus decimais.</summary>
public sealed record RotaQuery(
    double OrigemLat,
    double OrigemLng,
    double DestinoLat,
    double DestinoLng);

/// <summary>Distância e duração reais de rota de carro.</summary>
public sealed record RotaResultado(
    int DistanciaMetros,
    int DuracaoSegundos);

/// <summary>
/// Mede a rota real entre dois pontos (issue #1274). Google Routes API quando
/// configurada, NoOp caso contrário.
///
/// <para>
/// <strong>Best-effort</strong>: sem rota, timeout, 4xx/5xx ou JSON inválido →
/// <see langword="null"/>. Nunca lança. O <c>CalcularFreteUseCase</c> trata null
/// voltando à estimativa <c>haversine × FatorRota</c> (ADR-0017).
/// </para>
/// </summary>
public interface IRotaClient
{
    Task<RotaResultado?> MedirAsync(RotaQuery query, CancellationToken ct = default);
}
