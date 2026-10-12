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

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest)]
    [InlineData(System.Net.HttpStatusCode.UnprocessableEntity)]
    public async Task ErroHttpViraFalha(System.Net.HttpStatusCode status)
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("recusado", null, status));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeFalse();
        r.Desfecho.Should().Be(DesfechoEstornoPedido.Recusado);
        r.Erro.Should().Be(MercadoPagoEstornoPedidoGateway.CodigoRecusado);
    }

    // ── I3.1 (#1667): resposta perdida não é recusa ──

    [Fact]
    public async Task Estorno_com_timeout_e_Indeterminado()
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("timeout"));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeFalse("não há confirmação do estorno");
        r.Desfecho.Should().Be(DesfechoEstornoPedido.Indeterminado);
        r.Erro.Should().Be(MercadoPagoEstornoPedidoGateway.CodigoIndeterminado).And.NotBe(MercadoPagoEstornoPedidoGateway.CodigoRecusado);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.BadGateway)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    [InlineData(null)]
    public async Task Falha_do_provedor_ou_de_rede_e_Indeterminado(System.Net.HttpStatusCode? status)
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("falhou", null, status));

        (await Gateway().EstornarAsync("123", 30m, "oc-1")).Desfecho.Should().Be(DesfechoEstornoPedido.Indeterminado);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_propaga()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("cancelado"));

        var act = () => Gateway().EstornarAsync("123", 30m, "oc-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");
        r.Sucesso.Should().BeFalse();
        r.Desfecho.Should().Be(DesfechoEstornoPedido.Indeterminado, "estorno ainda em análise não é recusa");
    }

    [Fact]
    public async Task EstornoRejeitadoViraFalha()
    {
        _mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("r-9", 30m, "rejected"));

        var r = await Gateway().EstornarAsync("123", 30m, "oc-1");

        r.Sucesso.Should().BeFalse();
        r.Desfecho.Should().Be(DesfechoEstornoPedido.Recusado);
    }
}
