using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Frete;

/// <summary>
/// Frete pela cotação da Lalamove (#1882). Mesma geometria do <see cref="CalcularFreteUseCaseRaioTests"/>:
/// cozinha em (0,0), destino (0, 0.025) cai na faixa ate-5km (R$ 25,00) do raio.
/// </summary>
public class CalcularFreteUseCaseLalamoveTests
{
    private const string Slug = "casa-da-baba";
    private const string Cep = "05500000";

    private static StorefrontEntity Loja(int faixaGratisMetros = 500)
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), Slug, "Casa da Babá", 0m);
        s.Ativar();
        s.ConfigurarFreteRaio(0, 0, 1.4, faixaGratisMetros, 5000,
            """[{"id":"ate-2km","ateMetros":2000,"valorCentavos":1500},{"id":"ate-5km","ateMetros":5000,"valorCentavos":2500}]""");
        return s;
    }

    private static (CalcularFreteUseCase Uc, ICotacaoFreteExterna Cotador) Build(
        StorefrontEntity loja, GeocodeResultado geo, CotacaoFreteExterna? cotacao)
    {
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(loja);

        var cep = Substitute.For<ICepLookupClient>();
        cep.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult(Cep, "Rua X", "Bairro Y", "São Paulo", "SP"));

        var geocoding = Substitute.For<IGeocodingClient>();
        geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns(geo);

        var cotador = Substitute.For<ICotacaoFreteExterna>();
        cotador.CotarAsync(Arg.Any<Guid>(), Arg.Any<PontoCotacaoFrete>(), Arg.Any<PontoCotacaoFrete>(), Arg.Any<CancellationToken>())
            .Returns(cotacao);

        var uc = new CalcularFreteUseCase(lojas, Substitute.For<IFreteZonaRepository>(), cep, geocoding,
            Substitute.For<IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance, cotador);
        return (uc, cotador);
    }

    [Theory]
    [InlineData(1740, 1800)]
    [InlineData(1800, 1800)]
    [InlineData(1801, 1900)]
    public async Task Cotacao_vira_o_frete_arredondado_para_cima_no_real(int cotadoCentavos, int cobradoCentavos)
    {
        var (uc, _) = Build(Loja(), new GeocodeResultado(0, 0.025, Confiavel: true),
            new CotacaoFreteExterna("lalamove", cotadoCentavos, 4100));

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        dto.Valor.Should().Be(cobradoCentavos);
        dto.ZonaLabel.Should().Be("Entrega por moto");
        dto.DistanciaMetros.Should().Be(4100);
        dto.GeocodeConfiavel.Should().BeTrue();
    }

    [Fact]
    public async Task Cota_da_cozinha_ate_o_endereco_geocodificado_da_loja_certa()
    {
        var loja = Loja();
        var (uc, cotador) = Build(loja, new GeocodeResultado(0, 0.025, Confiavel: true),
            new CotacaoFreteExterna("lalamove", 1740, null));

        await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        await cotador.Received(1).CotarAsync(loja.EmpresaId,
            Arg.Is<PontoCotacaoFrete>(p => p.Lat == 0 && p.Lng == 0),
            Arg.Is<PontoCotacaoFrete>(p => p.Lat == 0 && p.Lng == 0.025 && p.Endereco.Contains("Rua X, 100")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_cotacao_vale_a_faixa_do_raio()
    {
        var (uc, _) = Build(Loja(), new GeocodeResultado(0, 0.025, Confiavel: true), cotacao: null);

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        dto.Valor.Should().Be(2500);
        dto.ZonaLabel.Should().Be("Entrega por raio");
    }

    [Fact]
    public async Task Fora_do_raio_segue_sem_cobertura_e_nem_cota()
    {
        var (uc, cotador) = Build(Loja(), new GeocodeResultado(0, 0.04, Confiavel: true),
            new CotacaoFreteExterna("lalamove", 1740, null));

        Func<Task> act = () => uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        await act.Should().ThrowAsync<CepSemCoberturaException>();
        await cotador.DidNotReceiveWithAnyArgs().CotarAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Faixa_gratis_segue_gratis_e_nem_cota()
    {
        var (uc, cotador) = Build(Loja(faixaGratisMetros: 5000), new GeocodeResultado(0, 0.025, Confiavel: true),
            new CotacaoFreteExterna("lalamove", 1740, null));

        var dto = await uc.ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        dto.Valor.Should().Be(0);
        dto.Gratis.Should().BeTrue();
        await cotador.DidNotReceiveWithAnyArgs().CotarAsync(default, default!, default!, default);
    }
}
