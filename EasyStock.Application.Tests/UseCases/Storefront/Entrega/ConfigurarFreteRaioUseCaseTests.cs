using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Entrega;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Entrega;

/// <summary>
/// Frete por raio gravado pela própria loja (I2.1, #1648). A coordenada da cozinha sai do
/// geocodificador do servidor e só entra com resultado confiável; faixa inválida não grava nada.
/// </summary>
public class ConfigurarFreteRaioUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly ICepLookupClient _ceps = Substitute.For<ICepLookupClient>();
    private readonly IGeocodingClient _geocoding = Substitute.For<IGeocodingClient>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public ConfigurarFreteRaioUseCaseTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: _empresaId, slug: "casa-da-baba", tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_loja);
        _ceps.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult("05500000", "Rua X", "Bairro Y", "São Paulo", "SP"));
        _geocoding.Disponivel.Returns(true);
        Geocode(new GeocodeResultado(-23.57, -46.71, Confiavel: true));
    }

    private void Geocode(GeocodeResultado? resultado) =>
        _geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns(resultado);

    private ConfigurarFreteRaioUseCase UseCase() => new(_lojas, _ceps, _geocoding, _uow);

    private static FreteRaioInput Input(params FreteRaioFaixaInput[] faixas) => new(
        Cep: "05500-000", Numero: "100", RaioMaxMetros: 5000, FaixaGratisMetros: 500, FatorRota: 1.4,
        Faixas: faixas.Length > 0 ? faixas : [new(2000, 1500), new(5000, 2500)]);

    private async Task NadaGravado()
    {
        _loja.CozinhaLat.Should().BeNull();
        _loja.FreteFaixasJson.Should().BeNull();
        await _lojas.DidNotReceive().UpdateAsync(Arg.Any<StorefrontEntity>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Endereco_confiavel_grava_cozinha_raio_e_faixas()
    {
        var resultado = await UseCase().ExecuteAsync(_empresaId, Input());

        _loja.CozinhaLat.Should().Be(-23.57);
        _loja.CozinhaLng.Should().Be(-46.71);
        _loja.FreteRaioMaxMetros.Should().Be(5000);
        _loja.FreteFaixaGratisMetros.Should().Be(500);
        _loja.FreteFatorRota.Should().Be(1.4);
        resultado.Faixas.Select(f => (f.AteMetros, f.ValorCentavos)).Should().Equal((2000, 1500), (5000, 2500));
        await _lojas.Received(1).UpdateAsync(_loja, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Geocodifica_o_endereco_do_cep_com_o_numero()
    {
        await UseCase().ExecuteAsync(_empresaId, Input());

        await _geocoding.Received(1).GeocodificarAsync(
            Arg.Is<GeocodeQuery>(q => q.Cep == "05500000" && q.Numero == "100" && q.Logradouro == "Rua X" && q.Cidade == "São Paulo"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Endereco_sem_geocode_confiavel_recusa_e_nao_grava(bool semResultado)
    {
        Geocode(semResultado ? null : new GeocodeResultado(-23.57, -46.71, Confiavel: false));

        var act = () => UseCase().ExecuteAsync(_empresaId, Input());

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await NadaGravado();
    }

    [Fact]
    public async Task Sem_geocodificador_recusa_com_erro_proprio_e_nao_grava()
    {
        _geocoding.Disponivel.Returns(false);

        var act = () => UseCase().ExecuteAsync(_empresaId, Input());

        await act.Should().ThrowAsync<GeocodificadorIndisponivelException>();
        await _geocoding.DidNotReceive().GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>());
        await NadaGravado();
    }

    [Theory]
    [InlineData(3000, 3000)] // mesma distância duas vezes
    [InlineData(5000, 2000)] // fora de ordem
    public async Task Faixas_sobrepostas_recusa(int primeira, int segunda)
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, Input(new(primeira, 1500), new(segunda, 2500)));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _geocoding.DidNotReceive().GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>());
        await NadaGravado();
    }

    [Fact]
    public async Task Faixa_dentro_da_distancia_gratis_recusa()
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, Input(new(400, 1500), new(5000, 2500)));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await NadaGravado();
    }

    [Fact]
    public async Task Faixas_que_nao_alcancam_o_raio_recusa()
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, Input(new(2000, 1500), new(4000, 2500)));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await NadaGravado();
    }

    [Theory]
    [InlineData("123", "100")]
    [InlineData("05500-000", " ")]
    public async Task Cep_ou_numero_invalido_recusa(string cep, string numero)
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, Input() with { Cep = cep, Numero = numero });

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await NadaGravado();
    }

    // ── I2.2 (#1651): reler o que foi gravado e mexer nas faixas sem redigitar a cozinha ──

    [Fact]
    public async Task Obter_sem_raio_configurado_devolve_vazio()
    {
        var config = await UseCase().ObterAsync(_empresaId);

        config.Configurado.Should().BeFalse();
        config.RaioMaxMetros.Should().BeNull();
        config.Faixas.Should().BeEmpty();
    }

    [Fact]
    public async Task Obter_devolve_o_que_foi_gravado()
    {
        await UseCase().ExecuteAsync(_empresaId, Input());

        var config = await UseCase().ObterAsync(_empresaId);

        config.Configurado.Should().BeTrue();
        config.RaioMaxMetros.Should().Be(5000);
        config.FaixaGratisMetros.Should().Be(500);
        config.FatorRota.Should().Be(1.4);
        config.Faixas.Select(f => (f.AteMetros, f.ValorCentavos)).Should().Equal((2000, 1500), (5000, 2500));
    }

    [Fact]
    public async Task Sem_endereco_mantem_a_cozinha_ja_gravada_e_troca_as_faixas()
    {
        await UseCase().ExecuteAsync(_empresaId, Input());
        _geocoding.ClearReceivedCalls();
        _geocoding.Disponivel.Returns(false); // manter a cozinha não depende do geocodificador

        await UseCase().ExecuteAsync(_empresaId, Input(new FreteRaioFaixaInput(5000, 1900)) with { Cep = null, Numero = "" });

        _loja.CozinhaLat.Should().Be(-23.57);
        _loja.CozinhaLng.Should().Be(-46.71);
        (await UseCase().ObterAsync(_empresaId)).Faixas.Select(f => f.ValorCentavos).Should().Equal(1900);
        await _geocoding.DidNotReceive().GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_endereco_e_sem_cozinha_gravada_recusa()
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, Input() with { Cep = null, Numero = null });

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await NadaGravado();
    }

    [Fact]
    public async Task Sem_vitrine_lanca()
    {
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns((StorefrontEntity?)null);

        var act = () => UseCase().ExecuteAsync(_empresaId, Input());

        await act.Should().ThrowAsync<LojaSemVitrineException>();
    }
}
