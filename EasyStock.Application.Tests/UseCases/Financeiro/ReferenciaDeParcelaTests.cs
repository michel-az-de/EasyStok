namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #1769: pagar ou receber uma parcela respondia erro interno em 7 de 7 tentativas no laboratório
/// (<c>value too long for type character varying(120)</c>): a referência do movimento de caixa tinha 125 caracteres.
/// </summary>
public class ReferenciaDeParcelaTests
{
    private static readonly Guid Conta = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Parcela = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Pagamento = Guid.Parse("99999999-8888-7777-6666-555555555555");

    [Theory]
    [InlineData("cp")]
    [InlineData("cr")]
    public void ReferenciaDoPagamentoCabeNaColuna(string origem)
    {
        var referencia = ReferenciaDeParcela.DoPagamento(origem, Conta, Parcela, Pagamento);

        referencia.Length.Should().BeLessThanOrEqualTo(ReferenciaDeParcela.TamanhoMaximo);
        referencia.Should().Be($"{origem}:11111111222233334444555555555555:parcela:aaaaaaaabbbbccccddddeeeeeeeeeeee:pag:99999999888877776666555555555555");
    }

    [Fact]
    public void ReferenciaDoPixCabeComOMaiorTxid()
    {
        var txid = new string('7', 35);

        var referencia = ReferenciaDeParcela.DoPix("cr", Conta, Parcela, txid);

        referencia.Length.Should().BeLessThanOrEqualTo(ReferenciaDeParcela.TamanhoMaximo);
        referencia.Should().EndWith(":txid:" + txid, "o txid inteiro continua localizável");
    }

    [Fact]
    public void ReferenciaNuncaPassaDoLimiteMesmoComValorForaDoPadrao()
    {
        ReferenciaDeParcela.DoPix("cr", Conta, Parcela, new string('x', 200)).Length
            .Should().Be(ReferenciaDeParcela.TamanhoMaximo);
    }

    [Fact]
    public void OFormatoAntigoPassavaDoLimite()
    {
        // O que o código escrevia antes: os três identificadores por extenso.
        $"cp:{Conta}:parcela:{Parcela}:pag:{Pagamento}".Length.Should().Be(125);
    }
}
