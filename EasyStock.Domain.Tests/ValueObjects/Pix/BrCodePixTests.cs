using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects.Pix;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Pix;

/// <summary>
/// #2233: BR Code estático (EMV do Banco Central) gerado no servidor, com paridade com o
/// <c>EasyStock.Console/src/infra/pix.js</c>. Só chaves fictícias.
/// </summary>
public class BrCodePixTests
{
    [Fact]
    public void Crc16_confere_com_o_vetor_padrao_do_CCITT_FALSE()
    {
        // Vetor de verificação do CRC-16/CCITT-FALSE (polinômio 0x1021, semente 0xFFFF).
        BrCodePix.Crc16("123456789").Should().Be("29B1");
    }

    [Fact]
    public void Reproduz_o_exemplo_do_manual_do_Banco_Central()
    {
        // Exemplo do Manual de Padrões para Iniciação do Pix (BCB): chave aleatória, sem valor, txid "***".
        var recebedor = RecebedorPix.Criar(
            ChavePix.Criar(TipoChavePix.Aleatoria, "123e4567-e12b-12d1-a456-426655440000"), "Fulano de Tal", "BRASILIA");

        BrCodePix.Estatico(recebedor, valor: null, txid: null).Should().Be(
            "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D");
    }

    [Fact]
    public void Tem_paridade_com_o_pix_js_do_Console()
    {
        // Saída de emitirCobrancaPix({ numeroPedido: '1234', valor: 57.9 }) do Console (chave de demonstração fictícia).
        var recebedor = RecebedorPix.Criar(
            ChavePix.Criar(TipoChavePix.Email, "contato@casadababa.com.br"), "CASA DA BABA", "SAO PAULO");

        BrCodePix.Estatico(recebedor, 57.9m, "CDB123401DYO").Should().Be(
            "00020126470014br.gov.bcb.pix0125contato@casadababa.com.br520400005303986540557.905802BR5912CASA DA BABA6009SAO PAULO62160512CDB123401DYO6304B33A");
    }

    [Fact]
    public void Codigo_gerado_termina_com_o_CRC_do_proprio_conteudo()
    {
        var codigo = BrCodePix.Estatico(Recebedor(), 1234.5m, "PEDAB12CD34");

        codigo.Should().Contain("54071234.50").And.Contain("0511PEDAB12CD34");
        BrCodePix.Crc16(codigo[..^4]).Should().Be(codigo[^4..]);
    }

    [Theory]
    [InlineData("12345678901234567890123456")] // 26 caracteres
    [InlineData("PED-123")]
    [InlineData("PED 123")]
    [InlineData("PEDé123")]
    public void Txid_acima_de_25_ou_nao_alfanumerico_e_recusado(string txid)
    {
        var acao = () => BrCodePix.Estatico(Recebedor(), 10m, txid);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Valor_tem_de_ser_positivo(decimal valor)
    {
        var acao = () => BrCodePix.Estatico(Recebedor(), valor, "PED1");

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Txid_do_pedido_e_o_codigo_curto_com_prefixo()
    {
        var pedido = Guid.Parse("ab12cd34-0000-0000-0000-000000000000");

        BrCodePix.TxidDoPedido(pedido).Should().Be("PEDAB12CD34");
    }

    private static RecebedorPix Recebedor() =>
        RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test"), "Loja Exemplo", "Sao Paulo");
}
