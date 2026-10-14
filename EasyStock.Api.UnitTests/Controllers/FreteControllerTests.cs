using System.Text.Json;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Contrato público do frete com o site (I2.2, #1651): os campos de distância entram sem tirar nenhum
/// dos que o site já lê. Cozinha em (0,0); geocode (0, 0.025) dá ~3892 m de rota (fator 1.4).
/// </summary>
public class FreteControllerTests
{
    private const string Slug = "casa-da-baba";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly StorefrontEntity _loja = StorefrontEntity.Criar(Guid.NewGuid(), Slug, "Casa da Babá", 0m);
    private readonly IFreteZonaRepository _zonas = Substitute.For<IFreteZonaRepository>();
    private readonly IGeocodingClient _geocoding = Substitute.For<IGeocodingClient>();
    private readonly IRotaClient _rotas = Substitute.For<IRotaClient>();

    private async Task<JsonElement> CalcularAsync()
    {
        _loja.Ativar();
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        var ceps = Substitute.For<ICepLookupClient>();
        ceps.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult("05500000", "Rua X", "Bairro Y", "São Paulo", "SP"));

        var controller = new FreteController(new CalcularFreteUseCase(
            lojas, _zonas, ceps, _geocoding, _rotas, NullLogger<CalcularFreteUseCase>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var resposta = await controller.CalcularFrete(Slug, "05500-000", "100", CancellationToken.None);

        var corpo = resposta.Should().BeOfType<OkObjectResult>().Subject.Value;
        return JsonSerializer.SerializeToElement(corpo, corpo!.GetType(), Json);
    }

    private void ComRaio() => _loja.ConfigurarFreteRaio(0, 0, 1.4, 500, 5000,
        """[{"id":"ate-2km","ateMetros":2000,"valorCentavos":1500},{"id":"ate-5km","ateMetros":5000,"valorCentavos":2500}]""");

    private void Geocode(double lng) =>
        _geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GeocodeResultado(0, lng, Confiavel: true));

    [Fact]
    public async Task Dto_traz_campos_de_distancia()
    {
        ComRaio();
        Geocode(0.025);

        var json = await CalcularAsync();

        json.GetProperty("geocodeConfiavel").GetBoolean().Should().BeTrue();
        json.GetProperty("distanciaMetros").GetInt32().Should().Be(3892);
        json.GetProperty("faixaId").GetString().Should().Be("ate-5km");
        json.GetProperty("gratis").GetBoolean().Should().BeFalse();
        json.GetProperty("foraDeCobertura").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Dto_usa_a_rota_medida_como_distancia()
    {
        ComRaio();
        Geocode(0.025);
        _rotas.MedirAsync(Arg.Any<RotaQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RotaResultado(DistanciaMetros: 300, DuracaoSegundos: 120));

        var json = await CalcularAsync();

        json.GetProperty("distanciaMetros").GetInt32().Should().Be(300);
        json.GetProperty("faixaId").GetString().Should().Be("gratis");
        json.GetProperty("gratis").GetBoolean().Should().BeTrue();
        json.GetProperty("valor").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Dto_nao_perde_nenhum_campo_que_o_site_ja_le()
    {
        ComRaio();
        Geocode(0.025);

        var json = await CalcularAsync();

        json.EnumerateObject().Select(p => p.Name).Should().Contain(
            ["zonaId", "valor", "valorFormatado", "etaLabel", "zonaLabel"]);
        json.TryGetProperty("tempoEstimadoMinutos", out _).Should().BeFalse("segue fora do JSON público");
    }

    [Fact]
    public async Task Frete_por_zona_diz_que_nao_houve_distancia()
    {
        _zonas.BuscarZonaPorCepAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FreteZona.CriarPorCep(_loja.Id, "Centro", "05000000", "05999999", 15m, 40, 0));

        var json = await CalcularAsync();

        json.GetProperty("valor").GetInt32().Should().Be(1500);
        json.GetProperty("geocodeConfiavel").GetBoolean().Should().BeFalse();
        json.GetProperty("distanciaMetros").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("faixaId").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("gratis").GetBoolean().Should().BeFalse();
        json.GetProperty("foraDeCobertura").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void CalcularFrete_tem_limite_por_IP()
    {
        // #1784: rota anônima que chama geocode e rota pagos; sem limite, variar o número gerava custo.
        typeof(FreteController).GetMethod(nameof(FreteController.CalcularFrete))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .Single().PolicyName.Should().Be("frete-publico");
    }
}
