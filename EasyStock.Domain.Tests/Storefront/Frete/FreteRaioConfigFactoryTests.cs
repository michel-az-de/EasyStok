using EasyStock.Domain.Storefront.Frete;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Domain.Tests.Storefront.Frete;

/// <summary>
/// Testes do mapeamento config persistida → <see cref="FreteRaioConfig"/>
/// (ADR-0017, issue #673 S3).
/// </summary>
public class FreteRaioConfigFactoryTests
{
    private static StorefrontEntity StorefrontBase() =>
        StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Babá", 0m);

    [Fact]
    public void Sem_config_retorna_null()
    {
        var s = StorefrontBase();
        Assert.Null(FreteRaioConfigFactory.TentarCriar(s));
    }

    [Fact]
    public void Config_completa_mapeia_para_FreteRaioConfig()
    {
        var s = StorefrontBase();
        s.ConfigurarFreteRaio(-23.5614, -46.6562, 1.4, 500, 5000,
            """[{"id":"ate-2km","ateMetros":2000,"valorCentavos":1500}]""");

        var config = FreteRaioConfigFactory.TentarCriar(s);

        Assert.NotNull(config);
        Assert.Equal(-23.5614, config!.Origem.Lat);
        Assert.Equal(-46.6562, config.Origem.Lng);
        Assert.Equal(1.4, config.FatorRota);
        Assert.Equal(500, config.FaixaGratisMetros);
        Assert.Equal(5000, config.RaioMaxMetros);
        Assert.Single(config.Faixas);
        Assert.Equal("ate-2km", config.Faixas[0].Id);
        Assert.Equal(2000, config.Faixas[0].AteMetros);
        Assert.Equal(1500, config.Faixas[0].ValorCentavos);
    }

    [Fact]
    public void Config_parcial_sem_coordenada_retorna_null()
    {
        var s = StorefrontBase();
        s.ConfigurarFreteRaio(-23.5, null, 1.4, 500, 5000, null); // sem lng
        Assert.Null(FreteRaioConfigFactory.TentarCriar(s));
    }

    [Fact]
    public void Faixas_invalidas_no_json_sao_filtradas()
    {
        var s = StorefrontBase();
        s.ConfigurarFreteRaio(-23.5, -46.6, 1.4, 500, 5000,
            """[{"id":"ok","ateMetros":2000,"valorCentavos":1500},{"id":"","ateMetros":0,"valorCentavos":-1}]""");

        var config = FreteRaioConfigFactory.TentarCriar(s);

        Assert.NotNull(config);
        Assert.Single(config!.Faixas); // a inválida foi filtrada
        Assert.Equal("ok", config.Faixas[0].Id);
    }

    [Fact]
    public void Json_corrompido_resulta_em_config_sem_faixas()
    {
        var s = StorefrontBase();
        s.ConfigurarFreteRaio(-23.5, -46.6, 1.4, 500, 5000, "{ nao eh array }");

        var config = FreteRaioConfigFactory.TentarCriar(s);

        Assert.NotNull(config);
        Assert.Empty(config!.Faixas);
    }
}
