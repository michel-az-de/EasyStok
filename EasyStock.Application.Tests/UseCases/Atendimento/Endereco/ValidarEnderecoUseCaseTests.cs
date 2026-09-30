using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Endereco;

public class ValidarEnderecoUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly IFreteZonaRepository _zonas = Substitute.For<IFreteZonaRepository>();
    private readonly ICepLookupClient _cep = Substitute.For<ICepLookupClient>();
    private readonly IConfiguracaoAtendimentoRepository _configuracao = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly StorefrontEntity _storefront;

    public ValidarEnderecoUseCaseTests()
    {
        _storefront = StorefrontEntity.Criar(empresaId: _empresaId, slug: "casa-da-baba", tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _storefront.Ativar();
        _storefronts.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_storefront);
        _storefronts.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(_storefront);
        _zonas.BuscarZonaPorCepAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FreteZona?)null);
        _cep.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult("05500000", "Rua Alvarenga", "Butantã", "São Paulo", "SP"));
        _configuracao.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));
    }

    private ValidarEnderecoUseCase UseCase()
    {
        var geocoding = Substitute.For<IGeocodingClient>();
        geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns((GeocodeResultado?)null);
        var frete = new CalcularFreteUseCase(_storefronts, _zonas, _cep, geocoding, NullLogger<CalcularFreteUseCase>.Instance);
        return new ValidarEnderecoUseCase(_storefronts, frete, _cep, _configuracao, NullLogger<ValidarEnderecoUseCase>.Instance);
    }

    private void ZonaCobre(string cep) =>
        _zonas.BuscarZonaPorCepAsync(_storefront.Id, cep, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FreteZona.CriarPorCep(storefrontId: _storefront.Id, label: "Oeste", cepInicio: "05000000",
                cepFim: "05999999", valor: 12.5m, tempoEstimadoMinutos: 40));

    [Fact]
    public async Task DentroDaZona()
    {
        ZonaCobre("05500000");

        var r = await UseCase().ExecuteAsync(new ValidarEnderecoInput(_empresaId, "05500-000", Numero: "120"));

        r.DentroDaArea.Should().BeTrue();
        r.TaxaEntrega.Should().Be(12.5m);
        r.TempoEstimadoMin.Should().Be(40);
        r.Motivo.Should().BeNull();
        r.EnderecoNormalizado.Cep.Should().Be("05500000");
        r.EnderecoNormalizado.Logradouro.Should().Be("Rua Alvarenga");
        r.EnderecoNormalizado.Bairro.Should().Be("Butantã");
        r.EnderecoNormalizado.Numero.Should().Be("120");
        r.EnderecoNormalizado.Completo.Should().BeTrue();
    }

    [Fact]
    public async Task ForaDaZonaSemExcecao()
    {
        var r = await UseCase().ExecuteAsync(new ValidarEnderecoInput(_empresaId, "01000-000", Numero: "1"));

        r.DentroDaArea.Should().BeFalse();
        r.Motivo.Should().Be("fora_area");
        r.MensagemForaArea.Should().NotBeNullOrWhiteSpace();
        r.TaxaEntrega.Should().BeNull();
    }

    [Fact]
    public async Task ViaCepIndisponivelDegrada()
    {
        ZonaCobre("05500000");
        _cep.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("fora"));

        var r = await UseCase().ExecuteAsync(new ValidarEnderecoInput(_empresaId, "05500000", Logradouro: "R. Alvarenga", Numero: "120"));

        r.DentroDaArea.Should().BeTrue();
        r.EnderecoNormalizado.Logradouro.Should().Be("R. Alvarenga");
        r.EnderecoNormalizado.Bairro.Should().BeNull();
        r.EnderecoNormalizado.Completo.Should().BeFalse();
    }

    [Fact]
    public async Task CepInvalidoNaoLanca()
    {
        var r = await UseCase().ExecuteAsync(new ValidarEnderecoInput(_empresaId, "123"));

        r.DentroDaArea.Should().BeFalse();
        r.Motivo.Should().Be("cep_invalido");
    }
}
