using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Geocoding;

/// <summary>
/// Cadeia de geocodificadores (I8.1, #1658): tenta cada provedor na ordem e fica com o primeiro
/// resultado confiável. Provedor fora do ar, sem resposta ou impreciso cai no seguinte.
///
/// <para>
/// Sem nenhum confiável, devolve o primeiro impreciso (o <c>CalcularFreteUseCase</c> cai para a zona,
/// como já fazia com um provedor só); sem resposta de ninguém, <see langword="null"/>. Cada elo tem o
/// próprio timeout, então o pior caso é a soma deles.
/// </para>
/// </summary>
public sealed class CadeiaGeocodingClient(
    IReadOnlyList<IGeocodingClient> elos,
    ILogger<CadeiaGeocodingClient> logger) : IGeocodingClient
{
    public bool Disponivel => elos.Any(e => e.Disponivel);

    public async Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        GeocodeResultado? impreciso = null;
        foreach (var elo in elos)
        {
            GeocodeResultado? resultado;
            try
            {
                resultado = await elo.GeocodificarAsync(query, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // O contrato diz que o elo não lança; se lançar, o checkout não cai. Só o tipo vai
                // para o log: a mensagem pode carregar a URL com a chave do provedor.
                logger.LogWarning(
                    "Geocodificador {Provedor} lançou {Excecao}; seguindo para o próximo.",
                    elo.GetType().Name, ex.GetType().Name);
                continue;
            }

            if (resultado is { Confiavel: true })
                return resultado;

            impreciso ??= resultado;
        }

        return impreciso;
    }
}
