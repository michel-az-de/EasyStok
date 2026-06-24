using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Frete;

/// <summary>
/// Testes do caminho de frete por raio no <see cref="CalcularFreteUseCase"/>
/// (ADR-0017, issue #673 S4). Origem da cozinha em (0,0); no equador 1° lng ≈
/// 111195 m, então geocode (0, 0.025) dá rota ~3892 m (fator 1.4) → faixa ate-5km.
/// </summary>
public class CalcularFreteUseCaseRaioTests
{
    private const string Slug = "casa-da-baba";
    private const string Cep = "05500000";

    private static StorefrontEntity StorefrontComRaio()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), Slug, "Casa da Babá", 0m);
        s.Ativar();
        s.ConfigurarFreteRaio(0, 0, 1.4, 500, 5000,
            """[{"id":"ate-2km","ateMetros":2000,"valorCentavos":1500},{"id":"ate-5km","ateMetros":5000,"valorCentavos":2500}]""");
        return s;
    }

    private static StorefrontEntity StorefrontSemRaio()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), Slug, "Casa da Babá", 0m);
        s.Ativar();
        return s;
    }

    private static EasyStock.Domain.Entities.Storefront.FreteZona ZonaPadrao() =>
        EasyStock.Domain.Entities.Storefront.FreteZona.CriarPorCep(
            Guid.NewGuid(), "Zona X", "05000000", "05999999", 15m, 40, 0);

    private static CalcularFreteUseCase Build(
        StorefrontEntity storefront,
        GeocodeResultado? geo,
        EasyStock.Domain.Entities.Storefront.FreteZona? zona)
    {
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(storefront);

        var zonaRepo = Substitute.For<IFreteZonaRepository>();
        zonaRepo.BuscarZonaPorCepAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(zona);

        var cep = Substitute.For<ICepLookupClient>();
        cep.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult(Cep, "Rua X", "Bairro Y", "São Paulo", "SP"));

        var geocoding = Substitute.For<IGeocodingClient>();
        geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns(geo);

        return new CalcularFreteUseCase(
            storefrontRepo, zonaRepo, cep, geocoding,
            NullLogger<CalcularFreteUseCase>.Instance);
    }

    [Fact]
    public async Task Geocode_confiavel_dentro_do_raio_usa_frete_raio()
    {
        var uc = Build(StorefrontComRaio(), new GeocodeResultado(0, 0.025, Confiavel: true), zona: null);

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        dto.Valor.Should().Be(2500); // faixa ate-5km (rota ~3892 m)
        dto.ZonaId.Should().Be(Guid.Empty);
        dto.ZonaLabel.Should().Be("Entrega por raio");
    }

    [Fact]
    public async Task Geocode_confiavel_fora_do_raio_lanca_sem_cobertura()
    {
        var uc = Build(StorefrontComRaio(), new GeocodeResultado(0, 0.04, Confiavel: true), zona: null);

        Func<Task> act = () => uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        await act.Should().ThrowAsync<CepSemCoberturaException>();
    }

    [Fact]
    public async Task Geocode_impreciso_cai_para_zona()
    {
        var zona = ZonaPadrao();
        var uc = Build(StorefrontComRaio(), new GeocodeResultado(0, 0.025, Confiavel: false), zona);

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        dto.ZonaId.Should().Be(zona.Id); // caiu pra zona
        dto.Valor.Should().Be(1500);     // 15,00 → 1500 centavos
    }

    [Fact]
    public async Task Sem_config_de_raio_usa_zona_mesmo_com_geocode_confiavel()
    {
        var zona = ZonaPadrao();
        var uc = Build(StorefrontSemRaio(), new GeocodeResultado(0, 0.025, Confiavel: true), zona);

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep));

        dto.ZonaId.Should().Be(zona.Id);
    }
}
