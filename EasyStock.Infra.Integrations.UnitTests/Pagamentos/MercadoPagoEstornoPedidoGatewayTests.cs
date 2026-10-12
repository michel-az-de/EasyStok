using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Integrations.UnitTests.Pagamentos;

/// <summary>S27 (#1186): o estorno da ocorrência vai ao Mercado Pago com a chave da ocorrência.</summary>
public class MercadoPagoEstornoPedidoGatewayTests
{
    private readonly IMercadoPagoClient _mp = Substitute.For<IMercadoPagoClient>();

    private MercadoPagoEstornoPedidoGateway Gateway() =>
        new(_mp, NullLogger<MercadoPagoEstornoPedidoGateway>.Instance);

    [Fact]
    public async Task RepassaPagamentoValorEChave()
    {
        _mp.EstornarAsync("123", 30m, "oc-1", Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("r-9", 30m, "approved"));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeTrue();
        r.IdSolicitacao.Should().Be("r-9");
    }

    [Fact]
    public async Task ErroHttpViraFalha()
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("400"));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeFalse();
        r.Erro.Should().Be(MercadoPagoEstornoPedidoGateway.CodigoRecusado);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("in_process")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Somente_aprovado_confirma_o_estorno(string? status)
    {
        _mp.EstornarAsync("123", 30m, "oc-1", Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("1234", 30m, status));
        (await Gateway().EstornarAsync("123", 30m, "oc-1")).Sucesso.Should().BeFalse();
    }

    [Fact]
    public async Task EstornoRejeitadoViraFalha()
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("r-9", 30m, "rejected"));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeFalse();
    }
}
