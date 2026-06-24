using EasyStock.Domain.Storefront.Frete;

namespace EasyStock.Domain.Tests.Storefront.Frete;

/// <summary>
/// Testes da calculadora pura de frete por raio (ADR-0017, issue #673 S1).
/// Origem fixa em (0,0); no equador 1° de longitude ≈ 111194.9 m, o que dá
/// distâncias controladas para exercitar grátis / faixas / fora de cobertura.
/// </summary>
public class FreteRaioCalculadoraTests
{
    private static FreteRaioConfig Config(double fator = 1.4) => new(
        Origem: new Coordenada(0, 0),
        FatorRota: fator,
        FaixaGratisMetros: 500,
        RaioMaxMetros: 5000,
        Faixas: new[]
        {
            new FreteFaixa("ate-2km", 2000, 1500),
            new FreteFaixa("ate-5km", 5000, 2500),
        });

    [Fact]
    public void Haversine_mesma_coordenada_eh_zero()
    {
        var d = FreteRaioCalculadora.HaversineMetros(new Coordenada(0, 0), new Coordenada(0, 0));
        Assert.Equal(0, d, 3);
    }

    [Fact]
    public void Haversine_um_grau_de_longitude_no_equador_aprox_111km()
    {
        var d = FreteRaioCalculadora.HaversineMetros(new Coordenada(0, 0), new Coordenada(0, 1));
        Assert.True(Math.Abs(d - 111194.9) < 50, $"esperado ~111194.9, obtido {d}");
    }

    [Fact]
    public void Distancia_rota_eh_linha_vezes_fator()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.01), Config(fator: 1.4));
        var esperado = (int)Math.Round(r.DistanciaMetros * 1.4, MidpointRounding.AwayFromZero);
        Assert.Equal(esperado, r.DistanciaRotaMetros);
    }

    [Fact]
    public void Perto_da_cozinha_eh_gratis()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.003), Config()); // rota ~468 m
        Assert.True(r.Gratis);
        Assert.Equal(0, r.ValorCentavos);
        Assert.Equal("gratis", r.FaixaId);
        Assert.False(r.ForaDeCobertura);
    }

    [Fact]
    public void Mesma_coordenada_da_cozinha_eh_gratis()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0), Config());
        Assert.True(r.Gratis);
        Assert.Equal(0, r.DistanciaMetros);
    }

    [Fact]
    public void Faixa_intermediaria_ate_2km()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.01), Config()); // rota ~1557 m
        Assert.False(r.Gratis);
        Assert.False(r.ForaDeCobertura);
        Assert.Equal("ate-2km", r.FaixaId);
        Assert.Equal(1500, r.ValorCentavos);
    }

    [Fact]
    public void Faixa_ate_5km()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.025), Config()); // rota ~3892 m
        Assert.Equal("ate-5km", r.FaixaId);
        Assert.Equal(2500, r.ValorCentavos);
    }

    [Fact]
    public void Acima_do_raio_eh_fora_de_cobertura()
    {
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.04), Config()); // rota ~6227 m
        Assert.True(r.ForaDeCobertura);
        Assert.Equal(0, r.ValorCentavos);
        Assert.Null(r.FaixaId);
        Assert.False(r.Gratis);
    }

    [Fact]
    public void Config_sem_faixa_que_cobre_trata_como_fora()
    {
        var config = Config() with { Faixas = new[] { new FreteFaixa("ate-1km", 1000, 1000) } };
        var r = FreteRaioCalculadora.Calcular(new Coordenada(0, 0.01), config); // rota ~1557 m, sem faixa que cubra
        Assert.True(r.ForaDeCobertura);
        Assert.Null(r.FaixaId);
    }
}
