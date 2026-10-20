using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Entrega;
using EasyStock.Application.UseCases.Storefront.Frete;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Entrega;

/// <summary>
/// "Testar um endereço" do frete por raio (I2.1, #1648): a conta é a do checkout, não uma cópia.
/// Cozinha em (0,0); geocode (0, 0.025) dá ~2780 m em linha reta e ~3892 m de rota (fator 1.4).
/// </summary>
public class TestarFreteRaioUseCaseTests
{
    private const string Slug = "casa-da-baba";
    private const string Cep = "05500000";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly ICepLookupClient _ceps = Substitute.For<ICepLookupClient>();
    private readonly IGeocodingClient _geocoding = Substitute.For<IGeocodingClient>();
    private readonly IRotaClient _rotas = Substitute.For<IRotaClient>();

    public TestarFreteRaioUseCaseTests()
    {
        _loja = StorefrontEntity.Criar(_empresaId, Slug, "Casa da Babá", 0m);
        _loja.Ativar();
        _loja.ConfigurarFreteRaio(0, 0, 1.4, 500, 5000,
            """[{"id":"ate-2km","ateMetros":2000,"valorCentavos":1500},{"id":"ate-5km","ateMetros":5000,"valorCentavos":2500}]""");
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_loja);
        _lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        _ceps.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult(Cep, "Rua X", "Bairro Y", "São Paulo", "SP"));
        _geocoding.Disponivel.Returns(true);
        Geocode(new GeocodeResultado(0, 0.025, Confiavel: true));
    }

    private void Geocode(GeocodeResultado? resultado) =>
        _geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns(resultado);

    private void Rota(RotaResultado? rota) =>
        _rotas.MedirAsync(Arg.Any<RotaQuery>(), Arg.Any<CancellationToken>()).Returns(rota);

    private TestarFreteRaioUseCase UseCase() => new(_lojas, _ceps, _geocoding, _rotas);

    private CalcularFreteUseCase Checkout() => new(
        _lojas, Substitute.For<IFreteZonaRepository>(), _ceps, _geocoding, _rotas,
        NullLogger<CalcularFreteUseCase>.Instance);

    [Theory]
    [InlineData(null, "ate-5km")] // sem rota medida: haversine × fator
    [InlineData(1800, "ate-2km")] // rota medida decide a faixa
    [InlineData(300, "gratis")]
    public async Task Mesma_conta_do_checkout(int? rotaMedidaMetros, string faixaEsperada)
    {
        Rota(rotaMedidaMetros is { } m ? new RotaResultado(DistanciaMetros: m, DuracaoSegundos: 600) : null);

        var teste = await UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));
        var checkout = await Checkout().ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        teste.Modo.Should().Be("raio");
        teste.FaixaId.Should().Be(faixaEsperada);
        teste.ValorCentavos.Should().Be(checkout.Valor);
        teste.DistanciaEstimada.Should().Be(rotaMedidaMetros is null);
        teste.DistanciaMetros.Should().Be(2780);
        teste.DistanciaRotaMetros.Should().Be(rotaMedidaMetros ?? 3892);
        teste.ForaDeCobertura.Should().BeFalse();
    }

    [Fact]
    public async Task Com_cotacao_lalamove_mostra_o_mesmo_valor_que_o_checkout_cobra()
    {
        var cotador = Substitute.For<ICotacaoFreteExterna>();
        cotador.CotarAsync(_empresaId, Arg.Any<PontoCotacaoFrete>(), Arg.Any<PontoCotacaoFrete>(), Arg.Any<CancellationToken>())
            .Returns(new CotacaoFreteExterna("lalamove", 1740, 4100));

        var teste = await new TestarFreteRaioUseCase(_lojas, _ceps, _geocoding, _rotas, cotador)
            .ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));
        var checkout = await new CalcularFreteUseCase(_lojas, Substitute.For<IFreteZonaRepository>(), _ceps, _geocoding, _rotas,
            NullLogger<CalcularFreteUseCase>.Instance, cotador).ExecuteAsync(new CalcularFreteInput(Slug, Cep, "100"));

        teste.FaixaId.Should().Be("lalamove");
        teste.ValorCentavos.Should().Be(1800);
        teste.ValorCentavos.Should().Be(checkout.Valor);
    }

    [Fact]
    public async Task Fora_do_raio_devolve_fora_de_cobertura_sem_valor()
    {
        Geocode(new GeocodeResultado(0, 0.04, Confiavel: true));

        var teste = await UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));

        teste.Modo.Should().Be("raio");
        teste.ForaDeCobertura.Should().BeTrue();
        teste.FaixaId.Should().BeNull();
        teste.ValorCentavos.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Geocode_nao_confiavel_cai_para_zona_como_no_checkout(bool semResultado)
    {
        Geocode(semResultado ? null : new GeocodeResultado(0, 0.025, Confiavel: false));

        var teste = await UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));

        teste.Modo.Should().Be("zona");
        teste.DistanciaMetros.Should().BeNull();
        teste.ValorCentavos.Should().BeNull();
        teste.Motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Raio_nao_configurado_cai_para_zona_sem_geocodificar()
    {
        _loja.ConfigurarFreteRaio(null, null, null, null, null, null);

        var teste = await UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));

        teste.Modo.Should().Be("zona");
        await _geocoding.DidNotReceive().GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_geocodificador_recusa_com_erro_proprio()
    {
        _geocoding.Disponivel.Returns(false);

        var act = () => UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput(Cep, "100"));

        await act.Should().ThrowAsync<GeocodificadorIndisponivelException>();
    }

    [Fact]
    public async Task Cep_invalido_recusa()
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, new TestarFreteRaioInput("123", "100"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
