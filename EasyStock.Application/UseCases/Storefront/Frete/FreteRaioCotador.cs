using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Domain.Storefront.Frete;

namespace EasyStock.Application.UseCases.Storefront.Frete;

/// <summary>Conta do frete por raio para um destino, com a rota medida e a cotação externa quando houve.</summary>
internal sealed record FreteRaioCotacao(FreteRaioResultado Resultado, RotaResultado? Rota, CotacaoFreteExterna? Externa = null)
{
    /// <summary>O que o cliente paga: a cotação externa arredondada (#1882) ou a faixa do raio.</summary>
    public int ValorCobradoCentavos => Externa is { } e ? FreteRaioCotador.ArredondarParaCimaNoReal(e.ValorCentavos) : Resultado.ValorCentavos;
}

/// <summary>Quem cota o frete externo e de onde sai a corrida.</summary>
internal sealed record CotacaoExternaDaLoja(ICotacaoFreteExterna Cotador, Guid EmpresaId, string EnderecoColeta);

/// <summary>
/// A conta única do frete por raio (ADR-0017): geocodifica o destino, mede a rota e aplica a
/// <see cref="FreteRaioCalculadora"/>. Com cotação externa (#1882), o raio continua decidindo cobertura e
/// faixa grátis, e o preço da corrida substitui o da faixa. O checkout e o "testar um endereço" da loja
/// chamam aqui, para que os dois nunca divirjam.
/// </summary>
internal static class FreteRaioCotador
{
    /// <summary><see langword="null"/> quando o geocode não veio ou veio impreciso: o caller cai para a zona.</summary>
    public static async Task<FreteRaioCotacao?> CotarAsync(
        FreteRaioConfig config,
        GeocodeQuery destino,
        IGeocodingClient geocodingClient,
        IRotaClient rotaClient,
        CancellationToken ct,
        CotacaoExternaDaLoja? externa = null)
    {
        var geo = await geocodingClient.GeocodificarAsync(destino, ct);
        if (geo is null || !geo.Confiavel)
            return null;

        // Rota real (issue #1274) quando disponível; null mantém haversine × fator.
        var rota = await rotaClient.MedirAsync(
            new RotaQuery(config.Origem.Lat, config.Origem.Lng, geo.Lat, geo.Lng), ct);

        var resultado = FreteRaioCalculadora.Calcular(
            new Coordenada(geo.Lat, geo.Lng), config, rota?.DistanciaMetros);

        // Fora da área ou grátis é decisão da loja: não cota.
        if (externa is null || resultado.ForaDeCobertura || resultado.Gratis)
            return new FreteRaioCotacao(resultado, rota);

        var cotacao = await externa.Cotador.CotarAsync(
            externa.EmpresaId,
            new PontoCotacaoFrete(config.Origem.Lat, config.Origem.Lng, externa.EnderecoColeta),
            new PontoCotacaoFrete(geo.Lat, geo.Lng, Endereco(destino)),
            ct);
        return new FreteRaioCotacao(resultado, rota, cotacao is { ValorCentavos: > 0 } ? cotacao : null);
    }

    /// <summary>Decisão do Felipe (17/10): 17,40 vira 18,00; valor redondo fica como está.</summary>
    public static int ArredondarParaCimaNoReal(int centavos) => (centavos + 99) / 100 * 100;

    private static string Endereco(GeocodeQuery d)
    {
        var rua = string.Join(", ", new[] { d.Logradouro, d.Numero }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var cidade = string.Join(" - ", new[] { d.Cidade, d.Uf }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(", ", new[] { rua, d.Bairro, cidade, d.Cep }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
