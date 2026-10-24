using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Services.Pedidos;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Pedidos;

public class EnderecoEntregaPedidoTests
{
    [Fact]
    public async Task Site_preserva_cep_e_numero_informados_sem_depender_do_cadastro()
    {
        var ceps = Substitute.For<ICepLookupClient>();
        ceps.LookupAsync("01001000", Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult("01001000", "Praça da Sé", "Sé", "São Paulo", "SP"));
        var geo = Substitute.For<IGeocodingClient>();
        var pedido = Pedido.Criar(Guid.NewGuid());
        await new EnderecoEntregaPedido(ceps, geo, TimeProvider.System, NullLogger<EnderecoEntregaPedido>.Instance)
            .RegistrarAsync(pedido, new(null, "120", "ap 32", null, null, null, "01001-000"));
        pedido.EnderecoEntrega.Should().Be("Praça da Sé, 120, ap 32 - Sé, São Paulo/SP - CEP 01001-000");
        pedido.CepEntrega.Should().Be("01001000");
        pedido.BairroEntrega.Should().Be("Sé");
        pedido.EntregaLat.Should().BeNull();
        pedido.EntregaLng.Should().BeNull();
        await geo.Received(1).GeocodificarAsync(new GeocodeQuery("Praça da Sé", "120", "Sé", "São Paulo", "SP", "01001000"),
            Arg.Any<CancellationToken>());
    }

    // #2007 (item 2): Entregas lê este texto; a UF digitada vai junto da cidade.
    [Fact]
    public async Task Texto_do_endereco_leva_a_uf_junto_da_cidade()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        await new EnderecoEntregaPedido(Substitute.For<ICepLookupClient>(), Substitute.For<IGeocodingClient>(), TimeProvider.System,
            NullLogger<EnderecoEntregaPedido>.Instance)
            .RegistrarAsync(pedido, new("Rua dos Pinheiros", "10", null, "Pinheiros", "São Paulo", " sp ", "05417-000"));
        pedido.EnderecoEntrega.Should().Be("Rua dos Pinheiros, 10 - Pinheiros, São Paulo/SP - CEP 05417-000");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ausencia_ou_imprecisao_do_geocode_preserva_texto_sem_inventar_coordenadas(bool impreciso)
    {
        var geo = Substitute.For<IGeocodingClient>();
        if (impreciso) geo.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GeocodeResultado(-23.5, -46.6, false));
        var pedido = Pedido.Criar(Guid.NewGuid());
        await new EnderecoEntregaPedido(Substitute.For<ICepLookupClient>(), geo, TimeProvider.System,
            NullLogger<EnderecoEntregaPedido>.Instance).RegistrarAsync(pedido, new("Rua confirmada", "10", null, null, null, null, null));
        pedido.EnderecoEntrega.Should().Be("Rua confirmada, 10");
        pedido.EnderecoConfirmadoEm.Should().NotBeNull();
        pedido.EntregaLat.Should().BeNull();
        pedido.EntregaLng.Should().BeNull();
    }
}
