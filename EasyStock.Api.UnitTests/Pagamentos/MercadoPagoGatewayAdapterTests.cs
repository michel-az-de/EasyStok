using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Async.Pagamentos;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>S32: o adapter do roteador de gateways passa a consultar e estornar pelo client do Mercado Pago.</summary>
public class MercadoPagoGatewayAdapterTests
{
    private readonly IMercadoPagoClient _client = Substitute.For<IMercadoPagoClient>();

    private MercadoPagoGatewayAdapter Adapter() =>
        new(new ConfigurationBuilder().Build(), _client, NullLogger<MercadoPagoGatewayAdapter>.Instance);

    [Theory]
    [InlineData("approved", StatusGateway.Confirmado)]
    [InlineData("pending", StatusGateway.Pendente)]
    [InlineData("in_process", StatusGateway.Pendente)]
    [InlineData("rejected", StatusGateway.Falhou)]
    [InlineData("cancelled", StatusGateway.Falhou)]
    [InlineData("refunded", StatusGateway.Estornado)]
    [InlineData("charged_back", StatusGateway.Estornado)]
    [InlineData("desconhecido", StatusGateway.Desconhecido)]
    public async Task ConsultarMapeiaOStatusDaFonte(string status, StatusGateway esperado)
    {
        _client.ConsultarPagamentoAsync("123", Arg.Any<CancellationToken>())
            .Returns(new PagamentoMercadoPago("123", status, null, null, 10m, null, null, null));

        (await Adapter().ConsultarAsync("123")).Should().Be(esperado);
    }

    [Fact]
    public async Task ConsultarComFalhaDevolveDesconhecido()
    {
        _client.ConsultarPagamentoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<PagamentoMercadoPago?>>(_ => throw new HttpRequestException("mp fora"));

        (await Adapter().ConsultarAsync("123")).Should().Be(StatusGateway.Desconhecido);
    }

    [Fact]
    public async Task EstornarDelegaAoClient()
    {
        _client.EstornarAsync("123", 10m, null, Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("r-1", 10m, "approved"));

        var r = await Adapter().EstornarAsync("123", 10m);

        r.Sucesso.Should().BeTrue();
        r.ProtocoloEstorno.Should().Be("r-1");
    }

    [Fact]
    public async Task EstornarComFalhaDevolveInsucesso()
    {
        _client.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<EstornoMercadoPagoResult>>(_ => throw new HttpRequestException("mp fora"));

        var r = await Adapter().EstornarAsync("123", 10m);

        r.Sucesso.Should().BeFalse();
    }
}
