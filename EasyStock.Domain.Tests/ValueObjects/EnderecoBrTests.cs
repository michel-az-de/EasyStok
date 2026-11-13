using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

/// <summary>
/// #2018: caracterização das cópias de CEP e bairro (FreteZona, CheckoutCoreService, ListarJanelasDisponiveis,
/// CalcularFrete e os "só dígitos" do endereço do pedido, da validação e da impressão): todas faziam o mesmo; agora é uma.
/// </summary>
public class EnderecoBrTests
{
    [Theory]
    [InlineData("05500-000", "05500000")]
    [InlineData(" 05500000 ", "05500000")]
    [InlineData("05500000", "05500000")]
    [InlineData("05.500-000", "05500000")]
    [InlineData("abc", "")]
    [InlineData("  ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizarCep_FicaSoComOsDigitos(string? entrada, string esperado) =>
        EnderecoBr.NormalizarCep(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("05500-000", true)]
    [InlineData("05500000", true)]
    [InlineData("0550000", false)]
    [InlineData("055000000", false)]
    [InlineData(null, false)]
    public void CepValido_ExigeOitoDigitosDepoisDaMascara(string? cep, bool esperado) =>
        EnderecoBr.CepValido(cep).Should().Be(esperado);

    [Theory]
    [InlineData("05500000", "05500-000")]
    [InlineData("05500-000", "05500-000")]
    [InlineData(" 1234 ", "1234")]
    [InlineData(null, "")]
    public void FormatarCep_PoeOHifenSoQuandoTemOitoDigitos(string? cep, string esperado) =>
        EnderecoBr.FormatarCep(cep).Should().Be(esperado);

    [Theory]
    [InlineData("Butantã", "butanta")]
    [InlineData("  VILA Madalena ", "vila madalena")]
    [InlineData("São João", "sao joao")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizarBairro_SemAcentoMinusculoESemEspacoNasPontas(string? bairro, string esperado) =>
        EnderecoBr.NormalizarBairro(bairro).Should().Be(esperado);
}
