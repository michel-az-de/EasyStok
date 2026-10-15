using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Entrega;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>Frete por raio no cadastro de entrega da loja (I2.1, #1648): o que vira qual status.</summary>
public class TenantVitrineEntregaControllerRaioTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly IGeocodingClient _geocoding = Substitute.For<IGeocodingClient>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly TenantVitrineEntregaController _controller;

    private static readonly FreteRaioInput Corpo = new("05500-000", "100", 5000, 500, 1.4, [new(2000, 1500), new(5000, 2500)]);

    public TenantVitrineEntregaControllerRaioTests()
    {
        _loja = StorefrontEntity.Criar(_empresaId, "casa-da-baba", "Casa da Babá", 0m);
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_loja);
        _geocoding.Disponivel.Returns(true);

        var janelas = Substitute.For<IJanelaEntregaRepository>();
        var ceps = Substitute.For<ICepLookupClient>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.EmpresaId.Returns(_empresaId);

        _controller = new TenantVitrineEntregaController(
            new CadastroJanelasEntregaUseCase(_lojas, janelas, _uow),
            new CadastroZonasFreteUseCase(_lojas, Substitute.For<IFreteZonaRepository>(), _uow),
            new CadastroBloqueiosEntregaUseCase(_lojas, Substitute.For<IBloqueioEntregaRepository>(), janelas, _uow, TimeProvider.System),
            new ConfigurarFreteRaioUseCase(_lojas, ceps, _geocoding, _uow),
            new TestarFreteRaioUseCase(_lojas, ceps, _geocoding, Substitute.For<IRotaClient>()),
            currentUser);
    }

    [Fact]
    public async Task Sem_geocodificador_responde_422_ao_gravar_e_ao_testar()
    {
        _geocoding.Disponivel.Returns(false);

        var gravar = await _controller.ConfigurarRaio(Corpo, CancellationToken.None);
        var testar = await _controller.TestarRaio(new TestarFreteRaioInput("05500-000", "100"), CancellationToken.None);

        gravar.Should().BeOfType<UnprocessableEntityObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        testar.Should().BeOfType<UnprocessableEntityObjectResult>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Endereco_nao_localizado_responde_400_e_nao_grava()
    {
        _geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns((GeocodeResultado?)null);

        var resultado = await _controller.ConfigurarRaio(Corpo, CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>();
        _loja.CozinhaLat.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Endereco_confiavel_grava_e_responde_200()
    {
        _geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GeocodeResultado(-23.57, -46.71, Confiavel: true));

        var resultado = await _controller.ConfigurarRaio(Corpo, CancellationToken.None);

        resultado.Should().BeOfType<OkObjectResult>();
        _loja.CozinhaLat.Should().Be(-23.57);
        await _uow.Received(1).CommitAsync();
    }
}
