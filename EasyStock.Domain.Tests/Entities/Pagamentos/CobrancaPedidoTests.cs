using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Pagamentos;

/// <summary>
/// Testes da <see cref="CobrancaPedido"/> (S11): a cobrança do pedido pelo Mercado Pago (link com
/// expiração) ou na entrega (sem link). O instante vem sempre por parâmetro.
/// </summary>
public class CobrancaPedidoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Pedido = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CobrancaPedido Online(int tentativa = 1) =>
        CobrancaPedido.CriarOnline(Empresa, Pedido, 25m, "pref-1", "https://mp/pref-1", Agora.AddMinutes(30), tentativa, Agora);

    [Fact]
    public void CriarOnline_NascePendenteComLinkEExpiracao()
    {
        var c = Online();

        c.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
        c.Status.Should().Be(StatusCobrancaPedido.Pendente);
        c.LinkPagamento.Should().Be("https://mp/pref-1");
        c.ExpiraEm.Should().Be(Agora.AddMinutes(30));
        c.Tentativa.Should().Be(1);
        c.EhOnline.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CriarOnline_TentativaForaDe1a2_Recusa(int tentativa)
    {
        var act = () => Online(tentativa);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CriarNaEntrega_NaoTemLinkNemExpira()
    {
        var c = CobrancaPedido.CriarNaEntrega(Empresa, Pedido, 25m, Agora);

        c.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        c.LinkPagamento.Should().BeNull();
        c.ExpiraEm.Should().BeNull();
        c.Venceu(Agora.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Venceu_SoDepoisDaExpiracao()
    {
        var c = Online();

        c.Venceu(Agora.AddMinutes(29)).Should().BeFalse();
        c.Venceu(Agora.AddMinutes(30)).Should().BeTrue();
    }

    [Fact]
    public void MarcarPaga_RegistraPagamentoEMetodo()
    {
        var c = Online();

        c.MarcarPaga("pay-9", 25m, "pix", Agora.AddMinutes(5));

        c.Status.Should().Be(StatusCobrancaPedido.Paga);
        c.PagamentoExternoId.Should().Be("pay-9");
        c.ValorPago.Should().Be(25m);
        c.MetodoPagamento.Should().Be("pix");
        c.PagaEm.Should().Be(Agora.AddMinutes(5));
    }

    [Fact]
    public void MarcarPaga_CanceladaAindaAceita_DinheiroRecebidoVence()
    {
        var c = Online();
        c.Cancelar("troca_forma", Agora);

        c.MarcarPaga("pay-9", 25m, "credito", Agora.AddMinutes(1));

        c.Status.Should().Be(StatusCobrancaPedido.Paga);
    }

    [Fact]
    public void MarcarPaga_JaPagaPorOutroPagamento_Recusa()
    {
        var c = Online();
        c.MarcarPaga("pay-1", 25m, "pix", Agora);

        var act = () => c.MarcarPaga("pay-2", 25m, "pix", Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Expirar_SoDePendente()
    {
        var c = Online();
        c.Expirar(Agora.AddMinutes(31));
        c.Status.Should().Be(StatusCobrancaPedido.Expirada);

        var paga = Online();
        paga.MarcarPaga("pay-1", 25m, "pix", Agora);
        var act = () => paga.Expirar(Agora.AddMinutes(31));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("pix", "bank_transfer", "pix")]
    [InlineData("visa", "credit_card", "credito")]
    [InlineData("debvisa", "debit_card", "debito")]
    [InlineData("bolbradesco", "ticket", "outro")]
    [InlineData(null, null, "outro")]
    public void MapearMetodo_TraduzOCodigoDoMercadoPago(string? metodo, string? tipo, string esperado) =>
        CobrancaPedido.MapearMetodo(metodo, tipo).Should().Be(esperado);
}
