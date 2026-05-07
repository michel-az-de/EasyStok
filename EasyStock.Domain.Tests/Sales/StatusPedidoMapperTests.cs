using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

public class StatusPedidoMapperTests
{
    [Theory]
    [InlineData(StatusPedido.Aguardando, "aguardando")]
    [InlineData(StatusPedido.Preparando, "preparando")]
    [InlineData(StatusPedido.Pronto, "pronto")]
    [InlineData(StatusPedido.Entregue, "entregue")]
    [InlineData(StatusPedido.Cancelado, "cancelado")]
    public void Format_retorna_string_canonica_lowercase(StatusPedido status, string esperado)
    {
        StatusPedidoMapper.Format(status).Should().Be(esperado);
    }

    [Fact]
    public void Format_status_invalido_lanca_ArgumentOutOfRange()
    {
        var statusInvalido = (StatusPedido)999;
        Action act = () => StatusPedidoMapper.Format(statusInvalido);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("aguardando", StatusPedido.Aguardando)]
    [InlineData("preparando", StatusPedido.Preparando)]
    [InlineData("pronto", StatusPedido.Pronto)]
    [InlineData("entregue", StatusPedido.Entregue)]
    [InlineData("cancelado", StatusPedido.Cancelado)]
    public void Parse_string_canonica_retorna_enum_correto(string raw, StatusPedido esperado)
    {
        StatusPedidoMapper.Parse(raw).Should().Be(esperado);
    }

    [Theory]
    [InlineData("AGUARDANDO")]
    [InlineData("Preparando")]
    [InlineData("PrOnTo")]
    public void Parse_e_case_insensitive(string raw)
    {
        Action act = () => StatusPedidoMapper.Parse(raw);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("  aguardando  ")]
    [InlineData("\tpreparando\n")]
    public void Parse_aplica_trim_em_whitespace(string raw)
    {
        Action act = () => StatusPedidoMapper.Parse(raw);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Parse_string_vazia_ou_whitespace_lanca(string? raw)
    {
        Action act = () => StatusPedidoMapper.Parse(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("desconhecido")]
    [InlineData("entregue_parcial")]
    [InlineData("draft")]
    public void Parse_status_desconhecido_lanca(string raw)
    {
        Action act = () => StatusPedidoMapper.Parse(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("desconhecido")]
    public void TryParse_retorna_false_em_invalido(string? raw)
    {
        var ok = StatusPedidoMapper.TryParse(raw, out var status);
        ok.Should().BeFalse();
        status.Should().Be(default(StatusPedido));
    }

    [Theory]
    [InlineData("aguardando", StatusPedido.Aguardando)]
    [InlineData("CANCELADO", StatusPedido.Cancelado)]
    public void TryParse_retorna_true_e_status_correto(string raw, StatusPedido esperado)
    {
        var ok = StatusPedidoMapper.TryParse(raw, out var status);
        ok.Should().BeTrue();
        status.Should().Be(esperado);
    }

    [Theory]
    [InlineData(StatusPedido.Aguardando)]
    [InlineData(StatusPedido.Preparando)]
    [InlineData(StatusPedido.Pronto)]
    [InlineData(StatusPedido.Entregue)]
    [InlineData(StatusPedido.Cancelado)]
    public void RoundTrip_Parse_de_Format_preserva_status(StatusPedido original)
    {
        var raw = StatusPedidoMapper.Format(original);
        var parsed = StatusPedidoMapper.Parse(raw);
        parsed.Should().Be(original);
    }
}
